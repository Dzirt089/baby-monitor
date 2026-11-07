using System.Runtime.Versioning;
using System.Text;
using System.Threading.Channels;
using Android.Content;
using Android.Net.Nsd;
using Android.Util;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Discovery;

/// <summary>
/// NSD-адаптер поиска Child-сервиса в mDNS. Обнаружение и resolve выполняются на фоновых
/// NSD-callback-потоках, peer-ы отдаются через bounded channel; Stop освобождает listener,
/// resolve-loop и multicast lock.
/// </summary>
public sealed class NsdPeerDiscovery : IPeerDiscovery
{
    private readonly Context _context;
    private readonly ILocalNetworkPermission _permission;

    /// <summary>Создаёт адаптер discovery.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="permission">Порт проверки разрешения на локальную сеть.</param>
    public NsdPeerDiscovery(Context context, ILocalNetworkPermission permission)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(permission);
        _context = context;
        _permission = permission;
    }

    /// <inheritdoc />
    public async Task<Result<IPeerDiscoverySession>> StartAsync(CancellationToken cancellationToken)
    {
        var permission = await _permission.EnsureGrantedAsync(cancellationToken);
        if (!permission.IsSuccess)
        {
            return Result<IPeerDiscoverySession>.Failure(permission.Error!);
        }

        var connectivityError = ConnectivityPrecondition.CheckLocalNetworkAvailable(_context);
        if (connectivityError is not null)
        {
            return Result<IPeerDiscoverySession>.Failure(connectivityError);
        }

        var nsd = _context.ApplicationContext?.GetSystemService(Context.NsdService) as NsdManager;
        if (nsd is null)
        {
            return Result<IPeerDiscoverySession>.Failure(
                new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD недоступен на устройстве."));
        }

        NsdPeerDiscoverySession? session = null;
        try
        {
            session = new NsdPeerDiscoverySession(_context, nsd);
            var error = await session.StartAsync(cancellationToken);
            if (error is not null)
            {
                await session.DisposeAsync();
                return Result<IPeerDiscoverySession>.Failure(error);
            }

            return Result<IPeerDiscoverySession>.Success(session);
        }
        catch (OperationCanceledException)
        {
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            throw;
        }
        catch (Exception exception)
        {
            Log.Warn("BabyMonitor.Nsd", "Discovery start failed: " + exception.GetType().Name);
            if (session is not null)
            {
                await session.DisposeAsync();
            }

            return Result<IPeerDiscoverySession>.Failure(
                new Error(DiscoveryErrorCodes.NsdUnavailable, "Не удалось запустить NSD discovery."));
        }
    }
}

internal sealed class NsdPeerDiscoverySession : IPeerDiscoverySession
{
    private const string LogTag = "BabyMonitor.Nsd";
    private const int PeerQueueCapacity = 16;
    private const int PendingResolveCapacity = 8;
    private const int MaxSeenServices = 128;

    private static readonly TimeSpan DiscoveryStartTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly NsdManager _nsd;
    private readonly MulticastLockScope _multicastLock;
    private readonly Channel<DiscoveredPeer> _peers;
    private readonly Channel<NsdServiceInfo> _pendingResolves;
    private readonly HashSet<string> _seenServices = new(StringComparer.Ordinal);
    private readonly object _seenLock = new();
    private readonly CancellationTokenSource _stopCts = new();
    private readonly TaskCompletionSource<Error?> _discoveryStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SemaphoreSlim _stopLock = new(1, 1);
    private int _state = (int)PeerDiscoveryState.Starting;
    private DiscoveryListener? _listener;
    private Task? _resolveLoop;
    private bool _discoveryActive;
    private int _stopped;

    public NsdPeerDiscoverySession(Context context, NsdManager nsd)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(nsd);
        _nsd = nsd;
        _multicastLock = new MulticastLockScope(context);
        _peers = Channel.CreateBounded<DiscoveredPeer>(
            new BoundedChannelOptions(PeerQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
        _pendingResolves = Channel.CreateBounded<NsdServiceInfo>(
            new BoundedChannelOptions(PendingResolveCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });
    }

    public PeerDiscoveryState State => (PeerDiscoveryState)Volatile.Read(ref _state);

    public Error? LastError { get; private set; }

    public event EventHandler<PeerDiscoveryStateChangedEventArgs>? StateChanged;

    public async Task<Error?> StartAsync(CancellationToken cancellationToken)
    {
        _listener = new DiscoveryListener(this);
        try
        {
            _nsd.DiscoverServices(PeerDiscoveryConstants.ServiceType, NsdProtocol.DnsSd, _listener);
            _discoveryActive = true;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "NSD DiscoverServices failed: " + exception.GetType().Name);
            return new Error(DiscoveryErrorCodes.NsdUnavailable, "Не удалось запустить NSD discovery.");
        }

        Error? startError;
        try
        {
            startError = await _discoveryStarted.Task.WaitAsync(DiscoveryStartTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD не подтвердил запуск discovery.");
        }

        if (startError is not null)
        {
            return startError;
        }

        _resolveLoop = Task.Run(() => ResolveLoopAsync(_stopCts.Token), CancellationToken.None);
        Volatile.Write(ref _state, (int)PeerDiscoveryState.Discovering);
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Discovering));
        return null;
    }

    public async IAsyncEnumerable<DiscoveredPeer> DiscoverPeersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var peer in _peers.Reader.ReadAllAsync(cancellationToken))
        {
            yield return peer;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopLock.WaitAsync(CancellationToken.None);
        try
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 1)
            {
                return;
            }

            try
            {
                await _stopCts.CancelAsync();
                if (_listener is not null && _discoveryActive)
                {
                    try
                    {
                        _nsd.StopServiceDiscovery(_listener);
                    }
                    catch (Exception exception)
                    {
                        Log.Warn(LogTag, "NSD StopServiceDiscovery failed: " + exception.GetType().Name);
                    }
                }

                _pendingResolves.Writer.TryComplete();
                _peers.Writer.TryComplete();

                if (_resolveLoop is not null)
                {
                    try
                    {
                        await _resolveLoop.WaitAsync(StopTimeout, cancellationToken);
                    }
                    catch (TimeoutException)
                    {
                        Log.Warn(LogTag, "NSD resolve loop did not stop in time.");
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
            }
            finally
            {
                _multicastLock.Dispose();
                _stopCts.Dispose();
                Volatile.Write(ref _state, (int)PeerDiscoveryState.Stopped);
                StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Stopped));
            }
        }
        finally
        {
            _stopLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }

    private async Task ResolveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var serviceInfo in _pendingResolves.Reader.ReadAllAsync(cancellationToken))
            {
                var peer = await TryResolveAsync(serviceInfo, cancellationToken);
                if (peer is not null)
                {
                    _peers.Writer.TryWrite(peer);
                }
                else
                {
                    // Resolve не удался: позволяем повторное обнаружение этого сервиса.
                    ForgetService(serviceInfo.ServiceName);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "NSD resolve loop failed: " + exception.GetType().Name);
            LastError = new Error(DiscoveryErrorCodes.ResolveFailed, "Ошибка resolve NSD-сервиса.");
            Volatile.Write(ref _state, (int)PeerDiscoveryState.Failed);
            StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Failed, LastError));
        }
    }

    private async Task<DiscoveredPeer?> TryResolveAsync(NsdServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            return await ResolveWithCallbackAsync(serviceInfo, cancellationToken);
        }

        return await ResolveLegacyAsync(serviceInfo, cancellationToken);
    }

    [SupportedOSPlatform("android34.0")]
    private async Task<DiscoveredPeer?> ResolveWithCallbackAsync(NsdServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        var executor = Java.Util.Concurrent.Executors.NewSingleThreadExecutor();
        ArgumentNullException.ThrowIfNull(executor);
        var callback = new ServiceInfoCallback(ResolveTimeout);
        try
        {
            _nsd.RegisterServiceInfoCallback(serviceInfo, executor, callback);
            var resolved = await callback.Result.Task.WaitAsync(ResolveTimeout, cancellationToken);
            return CreatePeer(resolved);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogTag, "NSD service info callback timed out.");
            return null;
        }
        finally
        {
            try
            {
                _nsd.UnregisterServiceInfoCallback(callback);
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "NSD UnregisterServiceInfoCallback failed: " + exception.GetType().Name);
            }

            executor.Shutdown();
            callback.Dispose();
            executor.Dispose();
        }
    }

    private async Task<DiscoveredPeer?> ResolveLegacyAsync(NsdServiceInfo serviceInfo, CancellationToken cancellationToken)
    {
        var resolver = new ResolveListener();
#pragma warning disable CA1422 // ResolveService устарел на API 34+, выше используется RegisterServiceInfoCallback.
        try
        {
            _nsd.ResolveService(serviceInfo, resolver);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "NSD ResolveService failed: " + exception.GetType().Name);
            return null;
        }
#pragma warning restore CA1422

        NsdServiceInfo? resolved;
        try
        {
            resolved = await resolver.Result.Task.WaitAsync(ResolveTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogTag, "NSD resolve timed out.");
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        finally
        {
            resolver.Dispose();
        }

        return CreatePeer(resolved);
    }

    private static DiscoveredPeer? CreatePeer(NsdServiceInfo? resolved)
    {
        if (resolved is null)
        {
            return null;
        }

        string? host;
        if (OperatingSystem.IsAndroidVersionAtLeast(34))
        {
            var addresses = resolved.HostAddresses;
            host = addresses is { Count: > 0 } ? addresses[0]?.HostAddress : null;
        }
        else
        {
#pragma warning disable CA1422 // Host устарел на API 34+; выше читаются HostAddresses.
            host = resolved.Host?.HostAddress;
#pragma warning restore CA1422
        }
        if (string.IsNullOrWhiteSpace(host) || resolved.Port is < 1 or > 65535)
        {
            return null;
        }

        var txt = new Dictionary<string, string>(StringComparer.Ordinal);
        var attributes = resolved.Attributes;
        if (attributes is not null)
        {
            foreach (var attribute in attributes)
            {
                var value = attribute.Value;
                if (value is not null)
                {
                    txt[attribute.Key] = Encoding.UTF8.GetString(value);
                }
            }
        }

        var parsed = PeerTxtRecords.Parse(txt);
        if (!parsed.IsSuccess)
        {
            Log.Warn(LogTag, "Peer TXT rejected: " + parsed.Error!.Code);
            return null;
        }

        try
        {
            return new DiscoveredPeer(
                resolved.ServiceName ?? "unknown",
                new TransportEndpoint(host, resolved.Port),
                parsed.Value.Role,
                parsed.Value.ProtocolVersion);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private void OnDiscoveryStarted()
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        _discoveryStarted.TrySetResult(null);
    }

    private void OnDiscoveryStartFailed(NsdFailure errorCode)
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        LastError = new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD discovery start failed: " + errorCode + ".");
        Volatile.Write(ref _state, (int)PeerDiscoveryState.Failed);
        _discoveryStarted.TrySetResult(LastError);
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Failed, LastError));
    }

    private void OnServiceFound(NsdServiceInfo serviceInfo)
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        var name = serviceInfo.ServiceName;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        lock (_seenLock)
        {
            if (!_seenServices.Add(name))
            {
                return;
            }

            // Ограничение на случай name-spam в LAN: набор не должен расти безгранично.
            if (_seenServices.Count > MaxSeenServices)
            {
                _seenServices.Clear();
                _seenServices.Add(name);
            }
        }

        _pendingResolves.Writer.TryWrite(serviceInfo);
    }

    private void ForgetService(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        lock (_seenLock)
        {
            _seenServices.Remove(name);
        }
    }

    private void OnServiceLost(NsdServiceInfo serviceInfo)
    {
        var name = serviceInfo.ServiceName;
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        lock (_seenLock)
        {
            _seenServices.Remove(name);
        }
    }

    private sealed class ServiceInfoCallback : Java.Lang.Object, NsdManager.IServiceInfoCallback
    {
        private readonly System.Threading.Timer _timeoutTimer;

        public ServiceInfoCallback(TimeSpan timeout)
        {
            _timeoutTimer = new System.Threading.Timer(
                _ => Result.TrySetResult(null),
                null,
                timeout,
                Timeout.InfiniteTimeSpan);
        }

        public TaskCompletionSource<NsdServiceInfo?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnServiceUpdated(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is not null)
            {
                Result.TrySetResult(serviceInfo);
            }
        }

        public void OnServiceLost()
        {
        }

        public void OnServiceInfoCallbackRegistrationFailed(NsdFailure errorCode) => Result.TrySetResult(null);

        public void OnServiceInfoCallbackUnregistered()
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timeoutTimer.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class DiscoveryListener : Java.Lang.Object, NsdManager.IDiscoveryListener
    {
        private readonly NsdPeerDiscoverySession _owner;

        public DiscoveryListener(NsdPeerDiscoverySession owner) => _owner = owner;

        public void OnDiscoveryStarted(string? serviceType) => _owner.OnDiscoveryStarted();

        public void OnServiceFound(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is not null)
            {
                _owner.OnServiceFound(serviceInfo);
            }
        }

        public void OnServiceLost(NsdServiceInfo? serviceInfo)
        {
            if (serviceInfo is not null)
            {
                _owner.OnServiceLost(serviceInfo);
            }
        }

        public void OnDiscoveryStopped(string? serviceType)
        {
        }

        public void OnStartDiscoveryFailed(string? serviceType, NsdFailure errorCode) => _owner.OnDiscoveryStartFailed(errorCode);

        public void OnStopDiscoveryFailed(string? serviceType, NsdFailure errorCode)
        {
        }
    }

    private sealed class ResolveListener : Java.Lang.Object, NsdManager.IResolveListener
    {
        public TaskCompletionSource<NsdServiceInfo?> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void OnServiceResolved(NsdServiceInfo? serviceInfo) => Result.TrySetResult(serviceInfo);

        public void OnResolveFailed(NsdServiceInfo? serviceInfo, NsdFailure errorCode) => Result.TrySetResult(null);
    }
}
