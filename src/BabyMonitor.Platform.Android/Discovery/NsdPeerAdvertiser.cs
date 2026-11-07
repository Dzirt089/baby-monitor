using Android.Content;
using Android.Net.Nsd;
using Android.Util;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Discovery;

/// <summary>
/// NSD-адаптер публикации Child-сервиса в mDNS. Multicast lock удерживается на время сессии
/// и гарантированно освобождается в Stop/Dispose.
/// </summary>
public sealed class NsdPeerAdvertiser : IPeerAdvertiser
{
    private readonly Context _context;
    private readonly ILocalNetworkPermission _permission;

    /// <summary>Создаёт адаптер advertise.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="permission">Порт проверки разрешения на локальную сеть.</param>
    public NsdPeerAdvertiser(Context context, ILocalNetworkPermission permission)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permission);
        _context = context;
        _permission = permission;
    }

    /// <inheritdoc />
    public async Task<Result<IPeerAdvertiserSession>> StartAsync(
        PeerAdvertisement advertisement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        var permission = await _permission.EnsureGrantedAsync(cancellationToken);
        if (!permission.IsSuccess)
        {
            return Result<IPeerAdvertiserSession>.Failure(permission.Error!);
        }

        var connectivityError = ConnectivityPrecondition.CheckLocalNetworkAvailable(_context);
        if (connectivityError is not null)
        {
            return Result<IPeerAdvertiserSession>.Failure(connectivityError);
        }

        var nsd = _context.ApplicationContext?.GetSystemService(Context.NsdService) as NsdManager;
        if (nsd is null)
        {
            return Result<IPeerAdvertiserSession>.Failure(
                new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD недоступен на устройстве."));
        }

        var session = new NsdPeerAdvertiserSession(_context, nsd, advertisement);
        try
        {
            var error = await session.StartAsync(cancellationToken);
            if (error is not null)
            {
                await session.DisposeAsync();
                return Result<IPeerAdvertiserSession>.Failure(error);
            }

            return Result<IPeerAdvertiserSession>.Success(session);
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }
}

internal sealed class NsdPeerAdvertiserSession : IPeerAdvertiserSession
{
    private const string LogTag = "BabyMonitor.Nsd";
    private static readonly TimeSpan RegistrationTimeout = TimeSpan.FromSeconds(10);

    private readonly Context _context;
    private readonly NsdManager _nsd;
    private readonly PeerAdvertisement _advertisement;
    private readonly TaskCompletionSource<Error?> _registration = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly MulticastLockScope _multicastLock;
    private int _state = (int)PeerAdvertisementState.Starting;
    private RegistrationListener? _listener;
    private volatile bool _registrationRequested;
    private int _stopped;

    public NsdPeerAdvertiserSession(Context context, NsdManager nsd, PeerAdvertisement advertisement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(nsd);
        ArgumentNullException.ThrowIfNull(advertisement);
        _context = context;
        _nsd = nsd;
        _advertisement = advertisement;
        _multicastLock = new MulticastLockScope(context);
    }

    public PeerAdvertisementState State => (PeerAdvertisementState)Volatile.Read(ref _state);

    public Error? LastError { get; private set; }

    public event EventHandler<PeerAdvertisementStateChangedEventArgs>? StateChanged;

    public async Task<Error?> StartAsync(CancellationToken cancellationToken)
    {
        _listener = new RegistrationListener(this);
        var serviceInfo = CreateServiceInfo();

        _registrationRequested = true;
        try
        {
            _nsd.RegisterService(serviceInfo, NsdProtocol.DnsSd, _listener);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "NSD RegisterService failed: " + exception.GetType().Name);
            return new Error(DiscoveryErrorCodes.NsdUnavailable, "Не удалось зарегистрировать NSD-сервис.");
        }

        try
        {
            return await _registration.Task.WaitAsync(RegistrationTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD не подтвердил регистрацию сервиса.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return Task.CompletedTask;
        }

        try
        {
            // UnregisterService вызывается всегда, если регистрация была запрошена: NSD может
            // подтвердить её позже, уже после отмены/таймаута StartAsync.
            if (_listener is not null && _registrationRequested)
            {
                try
                {
                    _nsd.UnregisterService(_listener);
                }
                catch (Exception exception)
                {
                    Log.Warn(LogTag, "NSD UnregisterService failed: " + exception.GetType().Name);
                }
            }
        }
        finally
        {
            _multicastLock.Dispose();
            _registration.TrySetResult(null);
            Volatile.Write(ref _state, (int)PeerAdvertisementState.Stopped);
            StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Stopped));
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private NsdServiceInfo CreateServiceInfo()
    {
        var serviceInfo = new NsdServiceInfo
        {
            ServiceName = NsdServiceNames.CreateInstanceName(),
            ServiceType = PeerDiscoveryConstants.ServiceType,
            Port = PeerDiscoveryConstants.ProvisionalTransportPort
        };

        foreach (var record in PeerTxtRecords.Create(_advertisement))
        {
            serviceInfo.SetAttribute(record.Key, record.Value);
        }

        return serviceInfo;
    }

    private void OnRegistered()
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            // Поздний callback после Stop: сервис уже снят/снимается, состояние не меняем.
            return;
        }

        _registration.TrySetResult(null);
        Volatile.Write(ref _state, (int)PeerAdvertisementState.Advertising);
        StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Advertising));
    }

    private void OnRegistrationFailed(NsdFailure errorCode)
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        var error = new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD registration failed: " + errorCode + ".");
        LastError = error;
        Volatile.Write(ref _state, (int)PeerAdvertisementState.Failed);
        _registration.TrySetResult(error);
        StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Failed, error));
    }

    private sealed class RegistrationListener : Java.Lang.Object, NsdManager.IRegistrationListener
    {
        private readonly NsdPeerAdvertiserSession _owner;

        public RegistrationListener(NsdPeerAdvertiserSession owner) => _owner = owner;

        public void OnServiceRegistered(NsdServiceInfo? serviceInfo) => _owner.OnRegistered();

        public void OnRegistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => _owner.OnRegistrationFailed(errorCode);

        public void OnServiceUnregistered(NsdServiceInfo? serviceInfo)
        {
        }

        public void OnUnregistrationFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode)
        {
        }
    }
}
