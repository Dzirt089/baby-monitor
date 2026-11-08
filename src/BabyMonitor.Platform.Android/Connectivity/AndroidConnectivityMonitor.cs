using Android.Content;
using Android.Net;
using Android.Util;
using BabyMonitor.Application.Abstractions.Connectivity;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Connectivity;

/// <summary>
/// Наблюдатель сети Android за портом <see cref="IConnectivityMonitor"/>: подписка на появление,
/// потерю и изменение возможностей Wi-Fi/Ethernet-сетей через <c>ConnectivityManager</c>.
/// </summary>
/// <remarks>
/// <para>
/// Используется reconnect-loop-ом: <see cref="NetworkChanged"/> поднимается при изменении набора
/// доступных LAN-сетей, чтобы отменить устаревшие connect/resolve попытки и перезапустить
/// discovery после смены сети (AGENTS.md §8, §11). Обработчики вызываются с binder-потоков
/// Android; подписчик не должен блокировать поток.
/// </para>
/// <para>
/// Privacy: снимок и события не содержат IP/MAC/SSID/имён сетей; логируются только типы ошибок.
/// <see cref="GetSnapshotAsync"/> после <see cref="DisposeAsync"/> возвращает типизированную ошибку
/// <see cref="RealtimeTransportErrorCodes.NetworkUnavailable"/> (а не исключение): монитор
/// вызывается из фонового reconnect-loop, где ожидаемые отказы выражаются <see cref="Result{T}"/>.
/// </para>
/// </remarks>
public sealed class AndroidConnectivityMonitor : IConnectivityMonitor
{
    private const string LogTag = "BabyMonitor.Network";

    private readonly ConnectivityManager? _connectivityManager;
    private readonly object _gate = new();
    private readonly HashSet<long> _lanNetworkHandles = [];
    private readonly LanTransportNetworkCallback _wifiCallback;
    private readonly LanTransportNetworkCallback _ethernetCallback;
    private Error? _registrationError;
    private bool _wifiRegistered;
    private bool _ethernetRegistered;
    private int _disposed;

    /// <summary>Создаёт монитор и подписывается на изменения Wi-Fi/Ethernet-сетей.</summary>
    /// <param name="context">Android context приложения (Activity не удерживается).</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public AndroidConnectivityMonitor(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _connectivityManager = context.ApplicationContext?.GetSystemService(Context.ConnectivityService) as ConnectivityManager
            ?? context.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        _wifiCallback = new LanTransportNetworkCallback(this, TransportType.Wifi);
        _ethernetCallback = new LanTransportNetworkCallback(this, TransportType.Ethernet);
        RegisterCallbacks();
    }

    /// <inheritdoc />
    public event EventHandler? NetworkChanged;

    /// <inheritdoc />
    public Task<Result<NetworkSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposed) == 1)
        {
            return Task.FromResult(Result<NetworkSnapshot>.Failure(new Error(
                RealtimeTransportErrorCodes.NetworkUnavailable,
                "Мониторинг сети остановлен.")));
        }

        var registrationError = Volatile.Read(ref _registrationError);
        if (registrationError is not null)
        {
            return Task.FromResult(Result<NetworkSnapshot>.Failure(registrationError));
        }

        bool hasLanNetwork;
        lock (_gate)
        {
            hasLanNetwork = _lanNetworkHandles.Count > 0;
        }

        return Task.FromResult(Result<NetworkSnapshot>.Success(new NetworkSnapshot(hasLanNetwork)));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        if (_wifiRegistered)
        {
            _wifiRegistered = false;
            UnregisterCallback(_wifiCallback);
        }

        if (_ethernetRegistered)
        {
            _ethernetRegistered = false;
            UnregisterCallback(_ethernetCallback);
        }

        lock (_gate)
        {
            _lanNetworkHandles.Clear();
        }

        _wifiCallback.Dispose();
        _ethernetCallback.Dispose();
        return ValueTask.CompletedTask;
    }

    private void RegisterCallbacks()
    {
        if (_connectivityManager is null)
        {
            _registrationError = new Error(
                RealtimeTransportErrorCodes.NetworkUnavailable,
                "Служба сетевого состояния недоступна на устройстве.");
            return;
        }

        try
        {
            // Два запроса по транспорту, а не один: AddTransportType объединяет типы через AND,
            // поэтому Wi-Fi и Ethernet подписываются отдельными callback-ами.
            var wifiRequest = CreateNetworkRequest(TransportType.Wifi);
            var ethernetRequest = CreateNetworkRequest(TransportType.Ethernet);
            if (wifiRequest is null || ethernetRequest is null)
            {
                _registrationError = new Error(
                    RealtimeTransportErrorCodes.NetworkUnavailable,
                    "Не удалось подготовить подписку на изменения сети.");
                return;
            }

            _connectivityManager.RegisterNetworkCallback(wifiRequest, _wifiCallback);
            _wifiRegistered = true;
            _connectivityManager.RegisterNetworkCallback(ethernetRequest, _ethernetCallback);
            _ethernetRegistered = true;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Network callback registration failed: " + exception.GetType().Name);
            _registrationError = new Error(
                RealtimeTransportErrorCodes.NetworkUnavailable,
                "Не удалось подписаться на изменения сети.");
        }
    }

    private void UnregisterCallback(LanTransportNetworkCallback callback)
    {
        if (_connectivityManager is null)
        {
            return;
        }

        try
        {
            _connectivityManager.UnregisterNetworkCallback(callback);
        }
        catch (Exception exception)
        {
            // Callback мог быть уже снят системой — повторная отписка не является отказом.
            Log.Warn(LogTag, "Network callback unregister failed: " + exception.GetType().Name);
        }
    }

    private static NetworkRequest? CreateNetworkRequest(TransportType transport)
    {
        var builder = new NetworkRequest.Builder();
        builder.AddTransportType(transport);
        return builder.Build();
    }

    private void OnNetworkAvailable(Network network, TransportType transport)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        if (!HasLanTransport(network, transport))
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = _lanNetworkHandles.Add(network.NetworkHandle);
        }

        if (changed)
        {
            RaiseNetworkChanged();
        }
    }

    private void OnNetworkLost(Network network)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        bool changed;
        lock (_gate)
        {
            changed = _lanNetworkHandles.Remove(network.NetworkHandle);
        }

        if (changed)
        {
            RaiseNetworkChanged();
        }
    }

    private void OnNetworkCapabilitiesChanged(
        Network network,
        NetworkCapabilities? capabilities,
        TransportType transport)
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        var hasLan = capabilities is not null && capabilities.HasTransport(transport);
        bool changed;
        lock (_gate)
        {
            // Событие поднимается только при реальном изменении состояния LAN-сети, а не на каждое
            // изменение возможностей (иначе reconnect-loop отменял бы операции без причины).
            changed = hasLan
                ? _lanNetworkHandles.Add(network.NetworkHandle)
                : _lanNetworkHandles.Remove(network.NetworkHandle);
        }

        if (changed)
        {
            RaiseNetworkChanged();
        }
    }

    private bool HasLanTransport(Network network, TransportType transport)
    {
        if (_connectivityManager is null)
        {
            return false;
        }

        try
        {
            var capabilities = _connectivityManager.GetNetworkCapabilities(network);
            return capabilities is not null && capabilities.HasTransport(transport);
        }
        catch (Exception exception)
        {
            // Сеть могла исчезнуть между callback-ами: не считаем её доступной.
            Log.Warn(LogTag, "Network capabilities read failed: " + exception.GetType().Name);
            return false;
        }
    }

    private void RaiseNetworkChanged()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            return;
        }

        NetworkChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class LanTransportNetworkCallback : ConnectivityManager.NetworkCallback
    {
        private readonly AndroidConnectivityMonitor _owner;
        private readonly TransportType _transport;

        internal LanTransportNetworkCallback(AndroidConnectivityMonitor owner, TransportType transport)
        {
            _owner = owner;
            _transport = transport;
        }

        public override void OnAvailable(Network network) =>
            _owner.OnNetworkAvailable(network, _transport);

        public override void OnLost(Network network) =>
            _owner.OnNetworkLost(network);

        public override void OnCapabilitiesChanged(Network network, NetworkCapabilities networkCapabilities) =>
            _owner.OnNetworkCapabilitiesChanged(network, networkCapabilities, _transport);
    }
}
