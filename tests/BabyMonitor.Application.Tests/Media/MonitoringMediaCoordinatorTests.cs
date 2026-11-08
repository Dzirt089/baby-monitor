using System.Collections.Concurrent;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Media;
using BabyMonitor.Application.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Application.Tests.Media;

/// <summary>
/// Тесты presentation-agnostic координатора media-сеанса: роли Child/Parent, media pipeline,
/// reconnect по ReconnectPolicy через fake TimeProvider, network callbacks, typed errors,
/// SnapshotChanged и Dispose. Все адаптеры — hand-written fakes, реального времени и sleep нет.
/// </summary>
public sealed class MonitoringMediaCoordinatorTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private sealed class Harness
    {
        public ManualTimeProvider Time { get; } = new();

        public FakePeerDiscovery Discovery { get; } = new();

        public FakePeerAdvertiser Advertiser { get; } = new();

        public FakeRealtimeTransport Transport { get; } = new();

        public FakeRealtimeTransportListener Listener { get; } = new();

        public FakeAudioCapture Capture { get; } = new();

        public FakeAudioPlayback Playback { get; } = new();

        public FakeAudioCodec Codec { get; } = new();

        public FakeConnectivityMonitor Connectivity { get; } = new();

        public MonitoringMediaCoordinator Create(
            DeviceRole role,
            ReconnectPolicy? policy = null,
            string? peerFingerprintSha256 = null,
            TimeSpan? heartbeatTimeout = null,
            TimeSpan? parentAlarmThreshold = null,
            TimeSpan? reconnectAttemptTimeout = null) =>
            new(
                role,
                Discovery,
                Advertiser,
                Transport,
                Listener,
                Capture,
                Playback,
                Codec,
                Connectivity,
                policy ?? CreatePolicy(),
                Time,
                peerFingerprintSha256,
                heartbeatTimeout,
                parentAlarmThreshold,
                reconnectAttemptTimeout);

        public static ReconnectPolicy CreatePolicy(int maxAttempts = 3) => new(
            maxAttempts: maxAttempts,
            initialDelay: TimeSpan.FromMilliseconds(10),
            maxDelay: TimeSpan.FromMilliseconds(50),
            jitterRatio: 0);
    }

    [Fact]
    public async Task Child_запускает_listener_и_публикует_advertisement()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaSessionState.Advertising, result.Value.State);
        Assert.Equal(DeviceRole.Child, result.Value.Role);
        Assert.True(result.Value.IsRunning);
        Assert.Single(harness.Listener.RequestedPorts);
        Assert.Equal(PeerDiscoveryConstants.ProvisionalTransportPort, harness.Listener.RequestedPorts[0]);
        Assert.Equal(1, harness.Advertiser.StartCalls);
        Assert.NotNull(harness.Advertiser.LastAdvertisement);
        Assert.Equal(DeviceRole.Child, harness.Advertiser.LastAdvertisement!.Role);
        Assert.Equal(PeerDiscoveryConstants.CurrentProtocolVersion, harness.Advertiser.LastAdvertisement.ProtocolVersion);
        Assert.Equal(0, harness.Playback.StartCalls);
    }

    [Fact]
    public async Task Child_кодирует_PCM_кадрами_20мс_и_отправляет_в_transport()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);

        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        Assert.Equal(new[] { AudioFormat.Default.Frame20MsBytes, AudioFormat.Default.Frame20MsBytes }, harness.Codec.GetEncodedPcmLengths());
        var sent = transport.GetSentFrames();
        Assert.Equal(2, sent.Count);
        Assert.All(sent, frame => Assert.Equal(harness.Codec.EncodedPayload, frame));

        var snapshot = coordinator.Snapshot!;
        Assert.Equal(MediaSessionState.MediaFlowing, snapshot.State);
        Assert.True(snapshot.Health!.CaptureActive);
        Assert.False(snapshot.Health.PlaybackActive);
        Assert.True(snapshot.Health.IsUserHearingAudio);
        Assert.Equal(0, harness.Playback.StartCalls);
    }

    [Fact]
    public async Task Child_Stop_останавливает_media_цикл_и_освобождает_ресурсы()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (transport, capture) = await StartFlowingChildAsync(harness, coordinator);
        var listenerSession = Assert.Single(harness.Listener.Sessions);
        var advertiserSession = harness.Advertiser.LastSession!;

        var stop = await coordinator.StopAsync(CancellationToken.None);

        Assert.True(stop.IsSuccess);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
        Assert.False(stop.Value.IsRunning);
        Assert.True(stop.Value.IsTerminal);
        Assert.Equal(1, capture.StopCalls);
        Assert.Equal(1, capture.DisposeCalls);
        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Equal(1, listenerSession.DisposeCalls);
        Assert.Equal(1, advertiserSession.StopCalls);
        Assert.Equal(1, advertiserSession.DisposeCalls);

        var secondStop = await coordinator.StopAsync(CancellationToken.None);
        Assert.True(secondStop.IsSuccess);
        Assert.Equal(MediaSessionState.Stopped, secondStop.Value.State);
    }

    [Fact]
    public async Task Child_capture_unavailable_даёт_Failed_с_типизированным_кодом()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        harness.Capture.StartError = new Error(AudioErrorCodes.CaptureUnavailable, "Микрофон занят другим приложением.");
        await using var coordinator = harness.Create(DeviceRole.Child);

        await coordinator.StartAsync(CancellationToken.None);
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(AudioErrorCodes.CaptureUnavailable, failed.Error!.Code);
        Assert.Equal(MediaSessionState.Failed, coordinator.Snapshot!.State);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.True(stop.IsSuccess);
        Assert.Equal(MediaSessionState.Failed, stop.Value.State);
    }

    [Fact]
    public async Task Child_несовпадение_BoundPort_даёт_Failed_без_ложного_advertisement()
    {
        var harness = new Harness();
        harness.Listener.BoundPort = 12345;
        await using var coordinator = harness.Create(DeviceRole.Child);

        var result = await coordinator.StartAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(MediaSessionErrorCodes.InvalidState, result.Error!.Code);
        Assert.Equal(0, harness.Advertiser.StartCalls);
        Assert.Equal(MediaSessionState.Failed, coordinator.Snapshot!.State);
    }

    [Fact]
    public async Task Child_потеря_transport_переходит_Degraded_Reconnecting_и_повторяет_попытку()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var states = new ConcurrentQueue<MediaSessionState>();
        await using var coordinator = harness.Create(DeviceRole.Child);
        coordinator.SnapshotChanged += (_, snapshot) => states.Enqueue(snapshot.State);
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        Assert.Contains(MediaSessionState.Degraded, states);
        Assert.Contains(MediaSessionState.Reconnecting, states);

        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 2, "повторная попытка подключения");
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        Assert.Equal(2, harness.Listener.StartCalls);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Child_исчерпание_попыток_переподключения_даёт_Failed_по_policy()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child, Harness.CreatePolicy(maxAttempts: 2));
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        transport.Fail(new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Peer недоступен."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        harness.Listener.EnqueueStartFailure(new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Повтор 1 не удался."));
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 2, "вторая попытка");
        await harness.Listener.WaitForStartAsync(2);

        harness.Listener.EnqueueStartFailure(new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Повтор 2 не удался."));
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 3, "третья попытка");
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, failed.Error!.Code);
        Assert.Equal(3, harness.Listener.StartCalls);
    }

    [Fact]
    public async Task Ручной_Stop_во_время_переподключения_отменяет_loop()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        var startCallsAfterStop = harness.Listener.StartCalls;
        harness.Time.Advance(TimeSpan.FromSeconds(60));
        await Task.Delay(20);
        Assert.Equal(startCallsAfterStop, harness.Listener.StartCalls);
        Assert.Equal(MediaSessionState.Stopped, coordinator.Snapshot!.State);
    }

    [Fact]
    public async Task Parent_запускает_discovery_и_подключается_к_найденному_child()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        await using var coordinator = harness.Create(DeviceRole.Parent);

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        Assert.Equal(MediaSessionState.Discovering, start.Value.State);
        Assert.Equal(1, harness.Discovery.StartCalls);

        var peer = CreatePeer();
        harness.Discovery.LastSession!.AddPeer(peer);

        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        Assert.Equal(1, harness.Transport.ConnectCalls);
        Assert.Equal(peer.Endpoint, harness.Transport.Endpoints[0]);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");

        transport.PushEncodedFrame(new byte[] { 1, 2, 3 });
        var flowing = await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        var written = harness.Playback.LastSession!.GetWrittenPcm();
        Assert.Single(written);
        Assert.Equal(AudioFormat.Default.Frame20MsBytes, written[0].Length);
        Assert.Equal(new[] { 3 }, harness.Codec.GetDecodedFrameLengths());
        Assert.Equal(0, harness.Capture.StartCalls);
        Assert.True(flowing.Health!.PlaybackActive);
        Assert.False(flowing.Health.CaptureActive);

        var playbackSession = harness.Playback.LastSession!;
        var discoverySession = Assert.Single(harness.Discovery.Sessions);
        var stop = await coordinator.StopAsync(CancellationToken.None);

        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
        Assert.Equal(1, playbackSession.StopCalls);
        Assert.Equal(1, playbackSession.DisposeCalls);
        Assert.Equal(1, transport.CloseCalls);
        Assert.Equal(1, transport.DisposeCalls);
        Assert.Equal(1, discoverySession.StopCalls);
        Assert.Equal(1, discoverySession.DisposeCalls);
    }

    [Fact]
    public async Task Parent_peer_unavailable_даёт_Failed_с_typed_ошибкой()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Endpoint детского устройства недоступен.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        // Один rogue candidate: после transient-отказа ждём следующий ограниченное время, затем
        // terminal SetupFailure с последней typed ошибкой (T007a).
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, failed.Error!.Code);
        Assert.Equal(1, harness.Transport.ConnectCalls);
    }

    [Fact]
    public async Task Parent_unauthorized_peer_даёт_Failed_с_typed_ошибкой()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.UnauthorizedPeer, "Peer не найден в trusted store.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, failed.Error!.Code);
    }

    [Fact]
    public async Task Parent_playback_unavailable_даёт_Failed_с_typed_ошибкой()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(new FakeRealtimeTransportSession()));
        harness.Playback.StartError = new Error(AudioErrorCodes.PlaybackUnavailable, "Нет audio focus.");
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(AudioErrorCodes.PlaybackUnavailable, failed.Error!.Code);
        Assert.Equal(0, harness.Capture.StartCalls);
    }

    [Fact]
    public async Task Parent_смена_сети_перезапускает_discovery()
    {
        var harness = new Harness();
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        Assert.Equal(1, harness.Discovery.StartCalls);

        harness.Connectivity.RaiseNetworkChanged();

        await harness.Discovery.WaitForStartAsync(2);
        Assert.Equal(2, harness.Discovery.StartCalls);
        Assert.Equal(MediaSessionState.Discovering, coordinator.Snapshot!.State);
        Assert.Equal(1, harness.Discovery.Sessions[0].StopCalls);
    }

    [Fact]
    public async Task Parent_смена_сети_во_время_media_flow_деградирует_сеанс()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        transport.PushEncodedFrame(new byte[] { 7 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        harness.Connectivity.RaiseNetworkChanged();
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        await AdvanceUntilAsync(harness.Time, () => harness.Discovery.StartCalls >= 2, "discovery перезапущен");
        Assert.Equal(2, harness.Discovery.StartCalls);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Child_runtime_ошибка_захвата_маппится_в_CaptureUnavailable()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        // Контракт Android-адаптера: runtime device error приходит как IOException.
        harness.Capture.ReadException = new IOException("AudioRecord device error.");
        await using var coordinator = harness.Create(DeviceRole.Child);

        await coordinator.StartAsync(CancellationToken.None);
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(AudioErrorCodes.CaptureUnavailable, failed.Error!.Code);
        Assert.Equal(MediaSessionState.Failed, coordinator.Snapshot!.State);
    }

    [Fact]
    public async Task Parent_runtime_ошибка_воспроизведения_маппится_в_PlaybackUnavailable()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        // Контракт Android-адаптера: runtime device error приходит как IOException.
        harness.Playback.WriteException = new IOException("AudioTrack device error.");
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);

        transport.PushEncodedFrame(new byte[] { 9 });
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(AudioErrorCodes.PlaybackUnavailable, failed.Error!.Code);
        Assert.Equal(0, harness.Capture.StartCalls);
    }

    [Fact]
    public async Task Child_окончание_потока_захвата_переводит_сеанс_в_Degraded()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (_, capture) = await StartFlowingChildAsync(harness, coordinator);

        capture.CompleteStream();
        await WaitForStateAsync(coordinator, MediaSessionState.Degraded);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Child_после_долгого_backoff_без_кадра_срабатывает_first_frame_watchdog()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var policy = new ReconnectPolicy(
            maxAttempts: 3,
            initialDelay: TimeSpan.FromMilliseconds(2500),
            maxDelay: TimeSpan.FromSeconds(3),
            jitterRatio: 0);
        await using var coordinator = harness.Create(DeviceRole.Child, policy);
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        // Backoff (2.5 с) больше media timeout (2 с). Новый attempt не получает ни одного кадра:
        // first-frame watchdog (не health-stall старого кадра) обязан перевести сеанс в reconnect.
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 2, "повторная попытка");
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Time.PendingTimerCount > 0, "first-frame watchdog");

        harness.Time.Advance(TimeSpan.FromSeconds(5));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);
        Assert.Equal(MediaSessionState.Reconnecting, coordinator.Snapshot!.State);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task SnapshotChanged_публикует_последовательность_и_не_поднимается_после_Dispose()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var states = new ConcurrentQueue<MediaSessionState>();
        var coordinator = harness.Create(DeviceRole.Child);
        coordinator.SnapshotChanged += (_, snapshot) => states.Enqueue(snapshot.State);

        await StartFlowingChildAsync(harness, coordinator);
        await coordinator.StopAsync(CancellationToken.None);
        var eventsBeforeDispose = states.Count;
        await coordinator.DisposeAsync();

        Assert.Equal(MediaSessionState.Starting, states.First());
        Assert.Equal(MediaSessionState.Stopped, states.Last());
        Assert.Contains(MediaSessionState.Advertising, states);
        Assert.Contains(MediaSessionState.Connected, states);
        Assert.Contains(MediaSessionState.MediaFlowing, states);
        Assert.Contains(MediaSessionState.Stopping, states);
        Assert.Equal(eventsBeforeDispose, states.Count);
    }

    [Fact]
    public async Task Starting_публикуется_до_остальных_состояний_даже_при_медленном_подписчике()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var states = new ConcurrentQueue<MediaSessionState>();
        using var advertisingPublished = new ManualResetEventSlim(false);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State is MediaSessionState.Starting)
            {
                // Старая реализация стартовала RunAsync до публикации Starting: даём ему шанс
                // опубликовать Advertising и зафиксировать инверсию порядка (T047).
                advertisingPublished.Wait(TimeSpan.FromMilliseconds(200));
            }
            else if (snapshot.State is MediaSessionState.Advertising)
            {
                advertisingPublished.Set();
            }

            states.Enqueue(snapshot.State);
        };

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        Assert.True(states.TryPeek(out var first));
        Assert.Equal(MediaSessionState.Starting, first);
    }

    [Fact]
    public async Task Stop_во_время_публикации_Starting_не_зависает_из_за_гейта()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        using var handlerEntered = new ManualResetEventSlim(false);
        using var releaseHandler = new ManualResetEventSlim(false);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State is MediaSessionState.Starting)
            {
                handlerEntered.Set();
                releaseHandler.Wait(TimeSpan.FromSeconds(5));
            }
        };

        var startTask = Task.Run(() => coordinator.StartAsync(CancellationToken.None));
        Assert.True(handlerEntered.Wait(TimeSpan.FromSeconds(5)));
        var stopTask = coordinator.StopAsync(CancellationToken.None);
        releaseHandler.Set();

        var stop = await stopTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(stop.IsSuccess);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        var start = await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(start.IsSuccess);
    }

    [Fact]
    public async Task DisposeAsync_идемпотентен_и_после_него_Start_Stop_дают_invalid_state()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var coordinator = harness.Create(DeviceRole.Child);
        await coordinator.StartAsync(CancellationToken.None);
        var listenerSession = Assert.Single(harness.Listener.Sessions);

        await coordinator.DisposeAsync();
        await coordinator.DisposeAsync();

        Assert.Equal(MediaSessionState.Stopped, coordinator.Snapshot!.State);
        Assert.Equal(1, listenerSession.DisposeCalls);

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.False(start.IsSuccess);
        Assert.Equal(MediaSessionErrorCodes.InvalidState, start.Error!.Code);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.False(stop.IsSuccess);
        Assert.Equal(MediaSessionErrorCodes.InvalidState, stop.Error!.Code);
    }

    [Fact]
    public async Task Stop_до_запуска_даёт_invalid_state()
    {
        var harness = new Harness();
        await using var coordinator = harness.Create(DeviceRole.Child);

        var stop = await coordinator.StopAsync(CancellationToken.None);

        Assert.False(stop.IsSuccess);
        Assert.Equal(MediaSessionErrorCodes.InvalidState, stop.Error!.Code);
        Assert.Null(coordinator.Snapshot);
    }

    [Fact]
    public async Task Snapshot_хранит_только_короткий_fingerprint_peer()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var fingerprint = new string('a', 64);
        await using var coordinator = harness.Create(DeviceRole.Child, peerFingerprintSha256: fingerprint);

        var start = await coordinator.StartAsync(CancellationToken.None);

        Assert.True(start.IsSuccess);
        Assert.Equal(fingerprint[..8], start.Value.PeerFingerprintShort);
        Assert.DoesNotContain(fingerprint, start.Value.ToString());
        Assert.NotNull(start.Value.SessionId);
    }

    [Fact]
    public void Конструктор_отклоняет_некорректный_fingerprint_peer()
    {
        var harness = new Harness();

        Assert.Throws<ArgumentException>(() => harness.Create(DeviceRole.Child, peerFingerprintSha256: "not-a-sha256"));
        Assert.Throws<ArgumentException>(() => harness.Create(DeviceRole.Unknown));
    }


    [Fact]
    public async Task Parent_без_первого_аудиокадра_уходит_в_reconnect_по_watchdog()
    {
        var harness = new Harness();
        var connectSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Transport.ConnectHandler = _ =>
        {
            connectSignal.TrySetResult();
            return Task.FromResult(Result<IRealtimeTransportSession>.Success(new FakeRealtimeTransportSession()));
        };
        await using var coordinator = harness.Create(DeviceRole.Parent);

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        Assert.Equal(MediaSessionState.Discovering, start.Value.State);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await connectSignal.Task.WaitAsync(TestTimeout);

        // Ни одного аудиокадра не приходит: watchdog обязан заметить «media path не подтвердился»
        // по истечении mediaTimeout (2 с) и уйти в reconnect, а не висеть в Connected.
        var reconnect = await WaitForStateAsyncWithTimeAsync(
            harness.Time,
            coordinator,
            MediaSessionState.Reconnecting);

        Assert.Equal(DeviceRole.Parent, reconnect.Role);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.True(stop.IsSuccess);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task MonitoringMediaSession_освобождает_codec_и_connectivity_при_dispose()
    {
        var harness = new Harness();
        var coordinator = harness.Create(DeviceRole.Child);
        var session = new MonitoringMediaSession(coordinator, harness.Codec, harness.Connectivity);

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(1, harness.Codec.DisposeCalls);
        Assert.Equal(1, harness.Connectivity.DisposeCalls);
    }

    [Fact]
    public async Task Heartbeat_поднимает_control_health_и_не_ломает_MediaFlowing()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (transport, capture) = await StartFlowingChildAsync(harness, coordinator);

        // Transport Connected, но heartbeat ещё не измерялся: health сохраняет транспортную оценку.
        Assert.Equal(SessionChannelState.Connected, coordinator.Snapshot!.Health!.Control);

        transport.RaiseHeartbeat();
        Assert.Equal(SessionChannelState.Connected, coordinator.Snapshot!.Health!.Control);

        // Heartbeat стареет (без срабатывания watchdog): control деградирует, media остаётся Flowing.
        for (var index = 0; index < 12; index++)
        {
            capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        var aged = coordinator.Snapshot!;
        Assert.Equal(MediaSessionState.MediaFlowing, aged.State);
        Assert.Equal(SessionChannelState.Degraded, aged.Health!.Control);

        transport.RaiseHeartbeat();
        var refreshed = coordinator.Snapshot!;
        Assert.Equal(SessionChannelState.Connected, refreshed.Health!.Control);
        Assert.Equal(MediaSessionState.MediaFlowing, refreshed.State);
        Assert.False(refreshed.ParentAlarmActive);
    }

    [Fact]
    public async Task Отсутствие_heartbeat_переводит_сеанс_в_Degraded_Reconnecting()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        var states = new ConcurrentQueue<MediaSessionState>();
        await using var coordinator = harness.Create(DeviceRole.Child);
        coordinator.SnapshotChanged += (_, snapshot) => states.Enqueue(snapshot.State);
        var (_, capture) = await StartFlowingChildAsync(harness, coordinator);

        // Media flow искусственно поддерживается, а heartbeat отсутствует: watchdog обязан сработать
        // по heartbeat-порогу (10 с) отдельно от media stall, а не ждать остановки кадров.
        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.State != MediaSessionState.Reconnecting)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались reconnect по realtime.heartbeat_missed.");
            }

            capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        Assert.Contains(MediaSessionState.Degraded, states);
        Assert.Contains(MediaSessionState.Reconnecting, states);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Heartbeat_missed_транспорта_даёт_Failed_с_typed_кодом()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child, Harness.CreatePolicy(maxAttempts: 1));
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);

        transport.Fail(new Error(RealtimeTransportErrorCodes.HeartbeatMissed, "Pong не пришёл."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        harness.Listener.EnqueueStartFailure(
            new Error(RealtimeTransportErrorCodes.HeartbeatMissed, "Повтор: pong не пришёл."));
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 2, "повторная попытка");
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.HeartbeatMissed, failed.Error!.Code);
        Assert.False(failed.ParentAlarmActive);
    }

    [Fact]
    public async Task ParentLossAlarm_поднимается_при_потере_peer_и_снимается_при_восстановлении()
    {
        var harness = new Harness();
        var transports = new List<FakeRealtimeTransportSession>();
        harness.Transport.ConnectHandler = _ =>
        {
            var session = new FakeRealtimeTransportSession();
            transports.Add(session);
            return Task.FromResult(Result<IRealtimeTransportSession>.Success(session));
        };
        var alarmSnapshots = new ConcurrentQueue<(MediaSessionState State, bool Alarm)>();
        await using var coordinator = harness.Create(DeviceRole.Parent, Harness.CreatePolicy(maxAttempts: 3));
        coordinator.SnapshotChanged += (_, snapshot) => alarmSnapshots.Enqueue((snapshot.State, snapshot.ParentAlarmActive));

        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        var transport = transports[0];
        transport.RaiseHeartbeat();
        transport.PushEncodedFrame(new byte[] { 1 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);
        Assert.False(coordinator.Snapshot!.ParentAlarmActive);

        // Heartbeat и media живы: 10 секунд виртуального времени тревога не поднимается.
        for (var index = 0; index < 20; index++)
        {
            transport.RaiseHeartbeat();
            transport.PushEncodedFrame(new byte[] { (byte)index });
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        Assert.Equal(MediaSessionState.MediaFlowing, coordinator.Snapshot!.State);
        Assert.False(coordinator.Snapshot.ParentAlarmActive);

        // Peer замолчал: media watchdog уводит сеанс в reconnect, а локальная тревога Parent
        // поднимается по собственному порогу (15 с) независимо от ответа Child.
        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.ParentAlarmActive != true)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("ParentLossAlarm не поднялась при потере peer-а.");
            }

            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        Assert.Contains(alarmSnapshots, entry => entry.Alarm && entry.State is not MediaSessionState.Failed);

        // Восстановление: следующая reconnect-попытка находит peer, media flow возвращается,
        // тревога снимается без терминального состояния.
        await AdvanceUntilAsync(
            harness.Time,
            () => harness.Discovery.StartCalls >= 2 && harness.Discovery.LastSession is not null,
            "повторный discovery");
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.Sessions.Count >= 2, "playback второго attempt");
        var recovered = transports[^1];
        recovered.RaiseHeartbeat();
        recovered.PushEncodedFrame(new byte[] { 2 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        Assert.False(coordinator.Snapshot!.ParentAlarmActive);
        Assert.Contains(alarmSnapshots, entry => !entry.Alarm && entry.State is MediaSessionState.MediaFlowing);

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
        Assert.False(stop.Value.ParentAlarmActive);
    }

    [Fact]
    public async Task Terminal_Failed_Parent_поднимает_ParentAlarmActive()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        var connectCalls = 0;
        harness.Transport.ConnectHandler = _ =>
        {
            connectCalls++;
            return connectCalls == 1
                ? Task.FromResult(Result<IRealtimeTransportSession>.Success(transport))
                : Task.FromResult(Result<IRealtimeTransportSession>.Failure(
                    new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Peer недоступен.")));
        };
        await using var coordinator = harness.Create(DeviceRole.Parent, Harness.CreatePolicy(maxAttempts: 1));
        await StartFlowingParentAsync(harness, coordinator, transport);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);
        await AdvanceUntilAsync(
            harness.Time,
            () => harness.Discovery.StartCalls >= 2 && harness.Discovery.LastSession is not null,
            "повторный discovery");
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, failed.Error!.Code);
        Assert.True(failed.ParentAlarmActive);
    }

    [Fact]
    public async Task Telemetry_считает_кадры_uptime_и_сбрасывается_при_новом_запуске()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        await StartFlowingChildAsync(harness, coordinator);

        var telemetry = coordinator.Snapshot!.Telemetry!;
        Assert.Equal(2, telemetry.SentFrames);
        Assert.Equal(0, telemetry.ReceivedFrames);
        Assert.Equal(0, telemetry.ReconnectAttempts);
        Assert.NotNull(telemetry.LastMediaFrameAt);

        harness.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(coordinator.Snapshot!.Telemetry!.Uptime >= TimeSpan.FromSeconds(1));

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        var restart = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(restart.IsSuccess);
        Assert.Equal(0, restart.Value.Telemetry!.SentFrames);
        Assert.Equal(0, restart.Value.Telemetry.ReconnectAttempts);
        Assert.Null(restart.Value.Telemetry.LastMediaFrameAt);
    }

    [Fact]
    public async Task Parent_telemetry_считает_принятые_кадры()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await StartFlowingParentAsync(harness, coordinator, transport);
        Assert.Equal(1, coordinator.Snapshot!.Telemetry!.ReceivedFrames);

        transport.PushEncodedFrame(new byte[] { 7 });
        await WaitUntilAsync(
            () => harness.Playback.LastSession!.GetWrittenPcm().Count >= 2,
            "второй кадр записан");

        var telemetry = coordinator.Snapshot!.Telemetry!;
        Assert.Equal(2, telemetry.ReceivedFrames);
        Assert.Equal(0, telemetry.SentFrames);
        Assert.NotNull(telemetry.LastMediaFrameAt);
    }

    [Fact]
    public async Task Telemetry_считает_попытки_переподключения()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (transport, _) = await StartFlowingChildAsync(harness, coordinator);
        Assert.Equal(0, coordinator.Snapshot!.Telemetry!.ReconnectAttempts);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.StartCalls >= 2, "вторая попытка");
        await WaitUntilAsync(
            () => coordinator.Snapshot?.Telemetry?.ReconnectAttempts == 1,
            "счётчик reconnect");

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(1, stop.Value.Telemetry!.ReconnectAttempts);
    }

    [Fact]
    public void MediaSessionTelemetry_отклоняет_отрицательные_значения()
    {
        Assert.Throws<ArgumentException>(() => new MediaSessionTelemetry(-1, 0, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new MediaSessionTelemetry(0, -1, 0, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new MediaSessionTelemetry(0, 0, -1, TimeSpan.Zero));
        Assert.Throws<ArgumentException>(() => new MediaSessionTelemetry(0, 0, 0, TimeSpan.FromSeconds(-1)));

        var telemetry = new MediaSessionTelemetry(3, 4, 5, TimeSpan.FromSeconds(6), DateTimeOffset.UnixEpoch);
        Assert.Equal(3, telemetry.SentFrames);
        Assert.Equal(4, telemetry.ReceivedFrames);
        Assert.Equal(5, telemetry.ReconnectAttempts);
        Assert.Equal(TimeSpan.FromSeconds(6), telemetry.Uptime);
        Assert.Equal(DateTimeOffset.UnixEpoch, telemetry.LastMediaFrameAt);
    }

    [Fact]
    public void MediaSessionSnapshot_тревога_Parent_допустима_только_для_Parent()
    {
        Assert.Throws<ArgumentException>(() => new MediaSessionSnapshot(
            DeviceRole.Child,
            MediaSessionState.Connected,
            parentAlarmActive: true));

        var parent = new MediaSessionSnapshot(
            DeviceRole.Parent,
            MediaSessionState.MediaFlowing,
            parentAlarmActive: true);
        Assert.True(parent.ParentAlarmActive);
        Assert.Null(parent.Telemetry);
    }

    [Fact]
    public async Task Heartbeat_порог_из_конструктора_используется()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child, heartbeatTimeout: TimeSpan.FromSeconds(3));
        var (_, capture) = await StartFlowingChildAsync(harness, coordinator);
        var startedAt = harness.Time.GetUtcNow();

        // Media flow поддерживается кадрами, heartbeat отсутствует: до настроенного порога 3 s живём.
        for (var index = 0; index < 4; index++)
        {
            capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        Assert.Equal(MediaSessionState.MediaFlowing, coordinator.Snapshot!.State);

        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.State != MediaSessionState.Reconnecting)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались reconnect по настроенному heartbeatTimeout.");
            }

            capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
            harness.Time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(5);
        }

        var elapsed = harness.Time.GetUtcNow() - startedAt;
        Assert.InRange(elapsed, TimeSpan.FromSeconds(2.9), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Parent_alarm_threshold_из_конструктора_используется()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        var alarmSnapshots = new ConcurrentQueue<(MediaSessionState State, bool Alarm)>();
        await using var coordinator = harness.Create(
            DeviceRole.Parent,
            parentAlarmThreshold: TimeSpan.FromSeconds(1));
        coordinator.SnapshotChanged += (_, snapshot) => alarmSnapshots.Enqueue((snapshot.State, snapshot.ParentAlarmActive));
        await StartFlowingParentAsync(harness, coordinator, transport);
        var aliveAt = harness.Time.GetUtcNow();

        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.ParentAlarmActive != true)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("ParentLossAlarm не поднялась по настроенному порогу 1 s.");
            }

            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        var elapsed = harness.Time.GetUtcNow() - aliveAt;
        Assert.InRange(elapsed, TimeSpan.FromSeconds(0.9), TimeSpan.FromSeconds(2));
        Assert.Contains(
            alarmSnapshots,
            entry => entry.Alarm && entry.State is MediaSessionState.MediaFlowing);
    }

    [Fact]
    public async Task Parent_media_тревога_поднимается_при_остановке_кадров_и_снимается_восстановлением()
    {
        var harness = new Harness();
        var transports = new List<FakeRealtimeTransportSession>();
        harness.Transport.ConnectHandler = _ =>
        {
            var session = new FakeRealtimeTransportSession();
            transports.Add(session);
            return Task.FromResult(Result<IRealtimeTransportSession>.Success(session));
        };
        var alarmSnapshots = new ConcurrentQueue<(MediaSessionState State, bool Alarm)>();
        await using var coordinator = harness.Create(DeviceRole.Parent);
        coordinator.SnapshotChanged += (_, snapshot) => alarmSnapshots.Enqueue((snapshot.State, snapshot.ParentAlarmActive));

        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        var transport = transports[0];
        transport.RaiseHeartbeat();
        transport.PushEncodedFrame(new byte[] { 1 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);
        Assert.False(coordinator.Snapshot!.ParentAlarmActive);

        // Heartbeat продолжает идти (control жив), кадров нет: watchdog фиксирует media loss и
        // поднимает media-тревогу Parent независимо от control-канала (FR-002 007).
        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.State is not (MediaSessionState.Degraded or MediaSessionState.Reconnecting))
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались media-тревоги Parent.");
            }

            transport.RaiseHeartbeat();
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        Assert.True(coordinator.Snapshot!.ParentAlarmActive);
        Assert.Contains(
            alarmSnapshots,
            entry => entry.Alarm
                && entry.State is (MediaSessionState.Degraded or MediaSessionState.Reconnecting));

        // Восстановление: reconnect находит peer, кадры снова идут — тревога снимается.
        await AdvanceUntilAsync(
            harness.Time,
            () => harness.Discovery.StartCalls >= 2 && harness.Discovery.LastSession is not null,
            "повторный discovery");
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.Sessions.Count >= 2, "playback второго attempt");
        var recovered = transports[^1];
        recovered.RaiseHeartbeat();
        recovered.PushEncodedFrame(new byte[] { 2 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        Assert.False(coordinator.Snapshot!.ParentAlarmActive);
        Assert.Contains(
            alarmSnapshots,
            entry => !entry.Alarm && entry.State is MediaSessionState.MediaFlowing);
    }

    [Fact]
    public async Task Первый_heartbeat_до_media_loop_не_теряется()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        var transport = await harness.Listener.WaitForAcceptAsync(1);

        await WaitForStateAsync(coordinator, MediaSessionState.Connected);

        // Pong приходит до старта capture/media loop: подписка уже активна в attempt (L7), а
        // последующий старт media loop не сбрасывает отметку heartbeat.
        transport.RaiseHeartbeat();

        await WaitUntilAsync(() => harness.Capture.LastSession is not null, "capture стартовал");
        var capture = harness.Capture.LastSession!;
        capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        Assert.Equal(SessionChannelState.Connected, coordinator.Snapshot!.Health!.Control);
    }

    [Fact]
    public async Task Live_telemetry_обновляется_без_смены_state_и_замирает_после_Stop()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var (_, capture) = await StartFlowingChildAsync(harness, coordinator);
        var liveSnapshots = new ConcurrentQueue<MediaSessionSnapshot>();
        coordinator.SnapshotChanged += (_, snapshot) => liveSnapshots.Enqueue(snapshot);
        var sentBefore = coordinator.Snapshot!.Telemetry!.SentFrames;
        var uptimeBefore = coordinator.Snapshot!.Telemetry!.Uptime;

        // 2.5 s виртуального времени: кадры поддерживают media flow; telemetry должна расти без
        // смены State за счёт периодического live-refresh (device-test v0.4.0).
        for (var index = 0; index < 5; index++)
        {
            capture.PushPcm(new byte[AudioFormat.Default.Frame20MsBytes]);
            harness.Time.Advance(TimeSpan.FromMilliseconds(500));
            await Task.Delay(5);
        }

        var live = coordinator.Snapshot!;
        Assert.Equal(MediaSessionState.MediaFlowing, live.State);
        Assert.True(live.Telemetry!.SentFrames >= sentBefore + 5);
        Assert.True(live.Telemetry.Uptime >= uptimeBefore + TimeSpan.FromSeconds(2));
        Assert.Contains(liveSnapshots, snapshot => snapshot.Telemetry!.SentFrames > sentBefore);
        Assert.All(liveSnapshots, snapshot => Assert.Equal(MediaSessionState.MediaFlowing, snapshot.State));

        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
        var eventsAfterStop = liveSnapshots.Count;

        // После Stop таймер live-refresh остановлен: время идёт, но снимков больше нет.
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(20);

        Assert.Equal(eventsAfterStop, liveSnapshots.Count);
    }

    [Fact]
    public async Task Повторное_подключение_ограничено_setup_timeout_и_уходит_в_Failed()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        await using var coordinator = harness.Create(
            DeviceRole.Parent,
            policy: Harness.CreatePolicy(maxAttempts: 1),
            reconnectAttemptTimeout: TimeSpan.FromSeconds(3));
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        transport.RaiseHeartbeat();
        transport.PushEncodedFrame(new byte[] { 1 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);
        await AdvanceUntilAsync(
            harness.Time,
            () => harness.Discovery.StartCalls >= 2 && harness.Discovery.LastSession is not null,
            "повторный discovery");

        // Повторная попытка висит в discovery без peer-а: setup timeout 3 s обязан завершить её
        // typed signaling_timeout → исчерпание MaxAttempts → terminal Failed.
        var deadline = DateTime.UtcNow + TestTimeout;
        while (coordinator.Snapshot?.State != MediaSessionState.Failed)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались Failed по setup timeout переподключения.");
            }

            harness.Time.Advance(TimeSpan.FromMilliseconds(100));
            await Task.Delay(5);
        }

        var failed = coordinator.Snapshot!;
        Assert.Equal(RealtimeTransportErrorCodes.SignalingTimeout, failed.Error!.Code);
        Assert.True(failed.ParentAlarmActive);
    }

    [Fact]
    public async Task Первичная_попытка_не_ограничена_reconnect_setup_timeout()
    {
        var harness = new Harness();
        await using var coordinator = harness.Create(
            DeviceRole.Parent,
            reconnectAttemptTimeout: TimeSpan.FromSeconds(1));
        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        Assert.Equal(MediaSessionState.Discovering, start.Value.State);

        // Первичный поиск Child не ограничен таймаутом переподключения: даже 5 s > 1 s не Failed.
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(20);

        Assert.Equal(MediaSessionState.Discovering, coordinator.Snapshot!.State);
        Assert.Null(coordinator.Snapshot.Error);
    }

    [Fact]
    public async Task Parent_alarm_не_поднимается_при_аудио_Failed()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(new FakeRealtimeTransportSession()));
        harness.Playback.StartError = new Error(AudioErrorCodes.PlaybackUnavailable, "Нет audio focus.");
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        // Локальный audio-сбой — не потеря peer-а: loss-тревога Parent не поднимается.
        Assert.Equal(AudioErrorCodes.PlaybackUnavailable, failed.Error!.Code);
        Assert.False(failed.ParentAlarmActive);
    }

    [Fact]
    public async Task Parent_alarm_поднимается_при_peer_unavailable_Failed()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Endpoint детского устройства недоступен.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, failed.Error!.Code);
        Assert.True(failed.ParentAlarmActive);
    }

    [Theory]
    [InlineData(RealtimeTransportErrorCodes.UnauthorizedPeer)]
    [InlineData(RealtimeTransportErrorCodes.UnsupportedVersion)]
    public async Task Parent_alarm_не_поднимается_при_отказе_доверия(string errorCode)
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(errorCode, "Peer отклонён до media-сессии.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        // Отказ доверия/версии — не потеря связи: loss-тревога не должна вводить пользователя в заблуждение.
        Assert.Equal(errorCode, failed.Error!.Code);
        Assert.False(failed.ParentAlarmActive);
    }

    [Theory]
    [InlineData(RealtimeTransportErrorCodes.TlsFailure)]
    [InlineData(RealtimeTransportErrorCodes.NetworkUnavailable)]
    [InlineData(RealtimeTransportErrorCodes.SessionClosed)]
    public async Task Parent_alarm_поднимается_при_loss_кодах_соединения(string errorCode)
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(errorCode, "Соединение потеряно.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);

        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(errorCode, failed.Error!.Code);
        Assert.True(failed.ParentAlarmActive);
        Assert.True(coordinator.Snapshot!.ParentAlarmActive);
    }

    [Fact]
    public async Task Parent_alarm_сохраняется_при_terminal_Failed_по_discovery_loss_после_retry()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        harness.Transport.ConnectHandler = _ =>
            Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        await using var coordinator = harness.Create(
            DeviceRole.Parent,
            policy: Harness.CreatePolicy(maxAttempts: 1));
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        transport.RaiseHeartbeat();
        transport.PushEncodedFrame(new byte[] { 1 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);

        transport.Fail(new Error(RealtimeTransportErrorCodes.MediaFlowLost, "Media-поток потерян."));
        await WaitForStateAsync(coordinator, MediaSessionState.Reconnecting);

        // Device-evidence (v0.4.0): Child потерял Wi-Fi, retry Parent упирается в discovery.wifi_unavailable.
        harness.Discovery.StartError = new Error(
            DiscoveryErrorCodes.WifiUnavailable,
            "Wi-Fi недоступен.");
        await AdvanceUntilAsync(harness.Time, () => harness.Discovery.StartCalls >= 2, "повторный discovery");
        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(DiscoveryErrorCodes.WifiUnavailable, failed.Error!.Code);
        Assert.True(failed.ParentAlarmActive);
        Assert.True(coordinator.Snapshot!.ParentAlarmActive);

        // FGS при terminal Failed обязан сохранить тревогу: terminal snapshot тоже с флагом.
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.True(stop.IsSuccess);
        Assert.Equal(MediaSessionState.Failed, stop.Value.State);
        Assert.True(stop.Value.ParentAlarmActive);
    }

    [Fact]
    public async Task Child_повторяет_accept_после_peer_level_отказа()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        harness.Listener.EnqueueAcceptFailure(new Error(RealtimeTransportErrorCodes.TlsFailure, "Rogue peer."));
        await using var coordinator = harness.Create(DeviceRole.Child);

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);

        // Пауза перед повтором Accept идёт через TimeProvider: двигаем виртуальное время.
        await AdvanceUntilAsync(
            harness.Time,
            () => harness.Listener.AcceptCalls >= 2 && harness.Listener.LastAcceptedTransport is not null,
            "повторный Accept");
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);

        Assert.Equal(2, harness.Listener.AcceptCalls);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Child_пять_peer_level_отказов_дают_FailStartup_с_последней_ошибкой()
    {
        var harness = new Harness();
        for (var index = 0; index < 5; index++)
        {
            harness.Listener.EnqueueAcceptFailure(new Error(
                RealtimeTransportErrorCodes.TlsFailure,
                "Rogue peer #" + index));
        }

        await using var coordinator = harness.Create(DeviceRole.Child);
        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);

        var failed = await WaitForStateAsyncWithTimeAsync(harness.Time, coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, failed.Error!.Code);
        Assert.Equal(5, harness.Listener.AcceptCalls);
    }

    [Fact]
    public async Task Child_Stop_во_время_повтора_accept_не_зависает()
    {
        var harness = new Harness();
        for (var index = 0; index < 5; index++)
        {
            harness.Listener.EnqueueAcceptFailure(new Error(
                RealtimeTransportErrorCodes.TlsFailure,
                "Rogue peer #" + index));
        }

        await using var coordinator = harness.Create(DeviceRole.Child);
        await coordinator.StartAsync(CancellationToken.None);
        await AdvanceUntilAsync(harness.Time, () => harness.Listener.AcceptCalls >= 1, "первый Accept");

        var stop = await coordinator.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Parent_перебирает_rogue_candidate_и_подключается_к_следующему()
    {
        var harness = new Harness();
        var transport = new FakeRealtimeTransportSession();
        var rogue = CreatePeer("rogue-child", "192.168.1.50");
        var good = CreatePeer("good-child", "192.168.1.51");
        var connectCalls = 0;
        harness.Transport.ConnectHandler = _ =>
        {
            connectCalls++;
            return connectCalls == 1
                ? Task.FromResult(Result<IRealtimeTransportSession>.Failure(
                    new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Rogue candidate.")))
                : Task.FromResult(Result<IRealtimeTransportSession>.Success(transport));
        };
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(rogue);
        harness.Discovery.LastSession.AddPeer(good);

        await WaitForStateAsync(coordinator, MediaSessionState.Connected);

        Assert.Equal(2, harness.Transport.ConnectCalls);
        Assert.Equal(2, harness.Transport.Endpoints.Count);
        Assert.Equal(rogue.Endpoint.Host, harness.Transport.Endpoints[0].Host);
        Assert.Equal(good.Endpoint.Host, harness.Transport.Endpoints[1].Host);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
    }

    [Fact]
    public async Task Parent_ограничивает_перебор_candidates_и_падает_с_последней_ошибкой()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Rogue candidate.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        for (var index = 0; index < 7; index++)
        {
            harness.Discovery.LastSession!.AddPeer(CreatePeer("rogue-" + index, "192.168.1." + (50 + index)));
        }

        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, failed.Error!.Code);
        Assert.Equal(5, harness.Transport.ConnectCalls);
    }

    [Fact]
    public async Task Parent_не_перебирает_candidates_при_non_retryable_ошибке()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.UnsupportedVersion, "Downlevel peer.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer("rogue-1", "192.168.1.50"));
        harness.Discovery.LastSession.AddPeer(CreatePeer("good", "192.168.1.51"));

        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        Assert.Equal(RealtimeTransportErrorCodes.UnsupportedVersion, failed.Error!.Code);
        Assert.Equal(1, harness.Transport.ConnectCalls);
    }

    [Fact]
    public async Task Конкурентный_Stop_не_публикует_Stopping_раньше_Starting()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var states = new ConcurrentQueue<MediaSessionState>();
        using var startingEntered = new ManualResetEventSlim(false);
        using var releaseStarting = new ManualResetEventSlim(false);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State is MediaSessionState.Starting)
            {
                startingEntered.Set();
                releaseStarting.Wait(TimeSpan.FromSeconds(5));
            }

            states.Enqueue(snapshot.State);
        };

        var startTask = Task.Run(() => coordinator.StartAsync(CancellationToken.None));
        Assert.True(startingEntered.Wait(TimeSpan.FromSeconds(5)));

        // Stop вызывается в окне, где Starting ещё не опубликован (handler заблокирован): обязан
        // дождаться публикации Starting, а не публиковать Stopping раньше (T048).
        var stopTask = coordinator.StopAsync(CancellationToken.None);
        await Task.Delay(50);
        Assert.DoesNotContain(MediaSessionState.Stopping, states);

        releaseStarting.Set();
        var stop = await stopTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        var start = await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(start.IsSuccess);

        Assert.True(states.TryPeek(out var first));
        Assert.Equal(MediaSessionState.Starting, first);
        Assert.Contains(MediaSessionState.Stopping, states);
    }

    [Fact]
    public async Task Stop_публикует_строгий_порядок_Starting_Stopping_Stopped_без_событий_после_terminal()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var states = new ConcurrentQueue<MediaSessionState>();
        coordinator.SnapshotChanged += (_, snapshot) => states.Enqueue(snapshot.State);

        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);
        var stop = await coordinator.StopAsync(CancellationToken.None);
        Assert.Equal(MediaSessionState.Stopped, stop.Value.State);

        var ordered = states.ToArray();
        Assert.Equal(MediaSessionState.Starting, ordered[0]);
        var stoppingIndex = Array.IndexOf(ordered, MediaSessionState.Stopping);
        var stoppedIndex = Array.IndexOf(ordered, MediaSessionState.Stopped);
        Assert.True(
            stoppingIndex > 0 && stoppedIndex > stoppingIndex,
            "Ожидался строгий порядок Starting < Stopping < Stopped, факт: " + string.Join(",", ordered));
        Assert.Equal(MediaSessionState.Stopped, ordered[^1]);

        var countAfterTerminal = states.Count;
        harness.Time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(20);
        Assert.Equal(countAfterTerminal, states.Count);
    }

    [Fact]
    public async Task Стресс_конкурентного_Stop_после_Starting_сохраняет_порядок_и_не_публикует_после_terminal()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        var states = new ConcurrentQueue<MediaSessionState>();

        for (var iteration = 0; iteration < 100; iteration++)
        {
            var startIndex = states.Count;
            using var startingEntered = new ManualResetEventSlim(false);
            using var releaseStarting = new ManualResetEventSlim(false);
            EventHandler<MediaSessionSnapshot> handler = (_, snapshot) =>
            {
                if (snapshot.State is MediaSessionState.Starting)
                {
                    startingEntered.Set();
                    releaseStarting.Wait(TimeSpan.FromSeconds(5));
                }

                states.Enqueue(snapshot.State);
            };
            coordinator.SnapshotChanged += handler;
            try
            {
                var startTask = Task.Run(() => coordinator.StartAsync(CancellationToken.None));
                Assert.True(
                    startingEntered.Wait(TimeSpan.FromSeconds(5)),
                    "iteration " + iteration + ": Starting handler не вызван");
                var stopTask = coordinator.StopAsync(CancellationToken.None);
                releaseStarting.Set();
                await Task.WhenAll(startTask, stopTask).WaitAsync(TimeSpan.FromSeconds(10));

                var slice = states.Skip(startIndex).ToArray();
                Assert.True(
                    slice.Length >= 3,
                    "iteration " + iteration + ": мало событий: " + string.Join(",", slice));
                Assert.Equal(MediaSessionState.Starting, slice[0]);
                var stopping = Array.IndexOf(slice, MediaSessionState.Stopping);
                var stopped = Array.IndexOf(slice, MediaSessionState.Stopped);
                Assert.True(
                    stopping > 0 && stopped > stopping,
                    "iteration " + iteration + " порядок: " + string.Join(",", slice));
                Assert.Equal(MediaSessionState.Stopped, slice[^1]);
            }
            finally
            {
                coordinator.SnapshotChanged -= handler;
            }
        }
    }

    [Fact]
    public async Task Stop_при_блокирующемся_Starting_подписчике_завершается_bounded()
    {
        var harness = new Harness();
        harness.Listener.TransportFactory = () => new FakeRealtimeTransportSession();
        await using var coordinator = harness.Create(DeviceRole.Child);
        using var startingEntered = new ManualResetEventSlim(false);
        using var releaseStarting = new ManualResetEventSlim(false);
        coordinator.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State is MediaSessionState.Starting)
            {
                startingEntered.Set();
                releaseStarting.Wait(TimeSpan.FromSeconds(30));
            }
        };

        var startTask = Task.Run(() => coordinator.StartAsync(CancellationToken.None));
        Assert.True(startingEntered.Wait(TimeSpan.FromSeconds(5)));
        var stopTask = coordinator.StopAsync(CancellationToken.None);

        try
        {
            // Подписчик не отпущен: Stop обязан сам принудительно открыть gate по
            // TimeProvider-таймауту и завершиться bounded (MEDIUM-B PR #26).
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (!stopTask.IsCompleted)
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new TimeoutException("Stop завис при блокирующемся Starting-подписчике.");
                }

                harness.Time.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(5);
            }

            var stop = await stopTask.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(MediaSessionState.Stopped, stop.Value.State);
        }
        finally
        {
            releaseStarting.Set();
        }

        var start = await startTask.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(start.IsSuccess);
    }

    [Fact]
    public async Task Parent_walk_non_OCE_ошибка_enumeration_даёт_adapter_failure_и_освобождает_discovery()
    {
        var harness = new Harness();
        harness.Transport.ConnectHandler = _ => Task.FromResult(Result<IRealtimeTransportSession>.Failure(
            new Error(RealtimeTransportErrorCodes.PeerUnavailable, "Rogue candidate.")));
        await using var coordinator = harness.Create(DeviceRole.Parent);
        await coordinator.StartAsync(CancellationToken.None);
        var discoverySession = harness.Discovery.LastSession!;
        discoverySession.EnumerationException = new InvalidOperationException("Malformed discovery stream.");
        discoverySession.AddPeer(CreatePeer("rogue-child", "192.168.1.50"));

        var failed = await WaitForStateAsync(coordinator, MediaSessionState.Failed);

        // Non-OCE из MoveNextAsync не должен превращаться в NotSupportedException/маскировать
        // исходную typed-ошибку; cleanup обязан освободить discovery (T051).
        Assert.Equal(MediaSessionErrorCodes.AdapterFailure, failed.Error!.Code);
        Assert.Equal(1, discoverySession.DisposeCalls);
    }

    private static DiscoveredPeer CreatePeer(string discoveryId, string host) => new(
        discoveryId,
        new TransportEndpoint(host, PeerDiscoveryConstants.ProvisionalTransportPort),
        DeviceRole.Child,
        PeerDiscoveryConstants.CurrentProtocolVersion);

    private static async Task<(FakeRealtimeTransportSession Transport, FakeAudioPlaybackSession Playback)> StartFlowingParentAsync(
        Harness harness,
        MonitoringMediaCoordinator coordinator,
        FakeRealtimeTransportSession transport)
    {
        await coordinator.StartAsync(CancellationToken.None);
        harness.Discovery.LastSession!.AddPeer(CreatePeer());
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Playback.LastSession is not null, "playback стартовал");
        transport.PushEncodedFrame(new byte[] { 1 });
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);
        return (transport, harness.Playback.LastSession!);
    }

    private static async Task<MediaSessionSnapshot> WaitForStateAsyncWithTimeAsync(
        ManualTimeProvider time,
        MonitoringMediaCoordinator coordinator,
        MediaSessionState state)
    {
        var signal = new TaskCompletionSource<MediaSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, MediaSessionSnapshot snapshot)
        {
            if (snapshot.State == state)
            {
                signal.TrySetResult(snapshot);
            }
        }

        coordinator.SnapshotChanged += Handler;
        try
        {
            if (coordinator.Snapshot is { } current && current.State == state)
            {
                return current;
            }

            var waitTask = signal.Task;
            var deadline = DateTime.UtcNow + TestTimeout;
            while (!waitTask.IsCompleted && DateTime.UtcNow < deadline)
            {
                time.Advance(TimeSpan.FromMilliseconds(500));
                await Task.Delay(5);
            }

            return await waitTask.WaitAsync(TestTimeout);
        }
        finally
        {
            coordinator.SnapshotChanged -= Handler;
        }
    }

    private static DiscoveredPeer CreatePeer() => new(
        "child-discovery-id",
        new TransportEndpoint("192.168.1.10", PeerDiscoveryConstants.ProvisionalTransportPort),
        DeviceRole.Child,
        PeerDiscoveryConstants.CurrentProtocolVersion);

    private static async Task<(FakeRealtimeTransportSession Transport, FakeAudioCaptureSession Capture)> StartFlowingChildAsync(
        Harness harness,
        MonitoringMediaCoordinator coordinator)
    {
        var start = await coordinator.StartAsync(CancellationToken.None);
        Assert.True(start.IsSuccess);

        var transport = await harness.Listener.WaitForAcceptAsync(1);
        await WaitForStateAsync(coordinator, MediaSessionState.Connected);
        await WaitUntilAsync(() => harness.Capture.LastSession is not null, "capture стартовал");

        var capture = harness.Capture.LastSession!;
        var frame = new byte[AudioFormat.Default.Frame20MsBytes];
        capture.PushPcm(frame, frame);
        await WaitForStateAsync(coordinator, MediaSessionState.MediaFlowing);
        return (transport, capture);
    }

    private static async Task<MediaSessionSnapshot> WaitForStateAsync(
        MonitoringMediaCoordinator coordinator,
        MediaSessionState state)
    {
        var signal = new TaskCompletionSource<MediaSessionSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, MediaSessionSnapshot snapshot)
        {
            if (snapshot.State == state)
            {
                signal.TrySetResult(snapshot);
            }
        }

        coordinator.SnapshotChanged += Handler;
        try
        {
            if (coordinator.Snapshot is { } current && current.State == state)
            {
                return current;
            }

            return await signal.Task.WaitAsync(TestTimeout);
        }
        finally
        {
            coordinator.SnapshotChanged -= Handler;
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались: " + description);
            }

            await Task.Delay(5);
        }
    }

    private static async Task AdvanceUntilAsync(ManualTimeProvider time, Func<bool> condition, string description)
    {
        var deadline = DateTime.UtcNow + TestTimeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Не дождались: " + description);
            }

            time.Advance(TimeSpan.FromMilliseconds(50));
            await Task.Delay(5);
        }
    }
}
