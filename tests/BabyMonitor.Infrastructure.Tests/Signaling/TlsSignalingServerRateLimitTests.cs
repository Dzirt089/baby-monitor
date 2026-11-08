using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// T007b: rate-limit listener-а после неудачного TLS/handshake. Cooldown отсчитывается через
/// инжектированный <see cref="TimeProvider"/> (fake-провайдеры ниже), не применяется на успешном
/// accept, прерывается caller-отменой (OCE) и Dispose (typed-отказ без зависания).
/// </summary>
public sealed class TlsSignalingServerRateLimitTests
{
    [Fact]
    public async Task Failed_handshake_выдерживает_cooldown_а_успешный_accept_не_тормозит()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var recordingTime = new RecordingTimeProvider();
        var options = new SignalingOptions(
            handshakeTimeout: TimeSpan.FromSeconds(5),
            timeProvider: recordingTime,
            failedHandshakeCooldown: TimeSpan.FromMilliseconds(300));
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            options,
            timeout.Token);
        await using var _ = serverSession;

        var rogueAcceptTask = serverSession.AcceptAsync(timeout.Token);
        await ConnectRogueTcpAndCloseAsync(serverSession.BoundPort, timeout.Token);
        var rogueResult = await rogueAcceptTask;

        Assert.False(rogueResult.IsSuccess, "Rogue TLS/handshake должен отклоняться.");
        Assert.Single(recordingTime.DelayRequests);
        Assert.Equal(options.FailedHandshakeCooldown, recordingTime.DelayRequests[0]);

        // Успешный accept обязан подниматься без cooldown-паузы.
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            peers.CreateParentIdentity(),
            options);
        var clientTask = client.ConnectAsync(timeout.Token);
        var acceptResult = await serverSession.AcceptAsync(timeout.Token);
        var clientResult = await clientTask;

        Assert.True(acceptResult.IsSuccess, "Listener должен принимать доверенного peer: " + acceptResult.Error?.Code);
        Assert.True(clientResult.IsSuccess);
        Assert.Single(recordingTime.DelayRequests); // cooldown на успешный accept не запрашивался
        await clientResult.Value.DisposeAsync();
        await acceptResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task Два_быстрых_rogue_подключения_обрабатываются_не_мгновенно_и_затем_успешный_peer()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var cooldown = TimeSpan.FromMilliseconds(150);
        var options = new SignalingOptions(
            handshakeTimeout: TimeSpan.FromSeconds(5),
            failedHandshakeCooldown: cooldown);
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            options,
            timeout.Token);
        await using var _ = serverSession;

        var firstTimer = Stopwatch.StartNew();
        var firstAcceptTask = serverSession.AcceptAsync(timeout.Token);
        await ConnectRogueTcpAndCloseAsync(serverSession.BoundPort, timeout.Token);
        var firstResult = await firstAcceptTask;
        firstTimer.Stop();

        var secondTimer = Stopwatch.StartNew();
        var secondAcceptTask = serverSession.AcceptAsync(timeout.Token);
        await ConnectRogueTcpAndCloseAsync(serverSession.BoundPort, timeout.Token);
        var secondResult = await secondAcceptTask;
        secondTimer.Stop();

        Assert.False(firstResult.IsSuccess);
        Assert.False(secondResult.IsSuccess);
        // Каждый отклонённый handshake выдерживает cooldown перед возвратом: второй rogue не
        // обрабатывается мгновенно (порог с запасом на гранулярность таймера).
        Assert.True(firstTimer.Elapsed >= TimeSpan.FromMilliseconds(100), "cooldown первого rogue: " + firstTimer.ElapsedMilliseconds + " ms");
        Assert.True(secondTimer.Elapsed >= TimeSpan.FromMilliseconds(100), "cooldown второго rogue: " + secondTimer.ElapsedMilliseconds + " ms");

        // Listener после cooldown-отказов по-прежнему принимает доверенного peer.
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            peers.CreateParentIdentity(),
            new SignalingOptions(TimeSpan.FromSeconds(5)));
        var clientTask = client.ConnectAsync(timeout.Token);
        var acceptResult = await serverSession.AcceptAsync(timeout.Token);
        var clientResult = await clientTask;

        Assert.True(acceptResult.IsSuccess, "Повторный Accept после cooldown: " + acceptResult.Error?.Code);
        Assert.True(clientResult.IsSuccess);
        await clientResult.Value.DisposeAsync();
        await acceptResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_во_время_cooldown_не_зависает_и_возвращает_известный_typed_отказ()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var blockingTime = new BlockingTimeProvider();
        var options = new SignalingOptions(
            handshakeTimeout: TimeSpan.FromSeconds(5),
            timeProvider: blockingTime,
            failedHandshakeCooldown: TimeSpan.FromSeconds(10));
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            options,
            timeout.Token);
        await using var _ = serverSession;

        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await ConnectRogueTcpAndCloseAsync(serverSession.BoundPort, timeout.Token);
        await blockingTime.TimerRequested.WaitAsync(TimeSpan.FromSeconds(10)); // cooldown начался

        var disposeTimer = Stopwatch.StartNew();
        await serverSession.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
        disposeTimer.Stop();
        var result = await acceptTask.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(disposeTimer.Elapsed < TimeSpan.FromSeconds(1), "Dispose не должен ждать cooldown: " + disposeTimer.ElapsedMilliseconds + " ms");
        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Caller_отмена_во_время_cooldown_даёт_OCE_без_ожидания_cooldown()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var blockingTime = new BlockingTimeProvider();
        var options = new SignalingOptions(
            handshakeTimeout: TimeSpan.FromSeconds(5),
            timeProvider: blockingTime,
            failedHandshakeCooldown: TimeSpan.FromSeconds(10));
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            options,
            timeout.Token);
        await using var _ = serverSession;
        using var cancellation = new CancellationTokenSource();

        var acceptTask = serverSession.AcceptAsync(cancellation.Token);
        await ConnectRogueTcpAndCloseAsync(serverSession.BoundPort, timeout.Token);
        await blockingTime.TimerRequested.WaitAsync(TimeSpan.FromSeconds(10)); // cooldown начался

        var cancelTimer = Stopwatch.StartNew();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await acceptTask.WaitAsync(TimeSpan.FromSeconds(2)));
        cancelTimer.Stop();

        Assert.True(cancelTimer.Elapsed < TimeSpan.FromSeconds(1), "Caller-отмена не должна ждать cooldown: " + cancelTimer.ElapsedMilliseconds + " ms");
    }

    [Fact]
    public void FailedHandshakeCooldown_по_умолчанию_250_мс_и_должен_быть_положительным()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(250), new SignalingOptions().FailedHandshakeCooldown);
        Assert.Equal(
            TimeSpan.FromMilliseconds(100),
            new SignalingOptions(failedHandshakeCooldown: TimeSpan.FromMilliseconds(100)).FailedHandshakeCooldown);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignalingOptions(failedHandshakeCooldown: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SignalingOptions(failedHandshakeCooldown: TimeSpan.FromMilliseconds(-1)));
    }

    /// <summary>Rogue-подключение: TCP connect и немедленное закрытие до TLS handshake.</summary>
    private static async Task ConnectRogueTcpAndCloseAsync(int port, CancellationToken cancellationToken)
    {
        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        tcpClient.Close();
    }

    /// <summary>TimeProvider, который записывает запрошенные паузы и завершает их немедленно.</summary>
    private sealed class RecordingTimeProvider : TimeProvider
    {
        private readonly object _sync = new();
        private readonly List<TimeSpan> _delayRequests = [];

        public IReadOnlyList<TimeSpan> DelayRequests
        {
            get
            {
                lock (_sync)
                {
                    return _delayRequests.ToArray();
                }
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_sync)
            {
                _delayRequests.Add(dueTime);
            }

            ThreadPool.QueueUserWorkItem(_ => callback(state));
            return new NoopTimer();
        }
    }

    /// <summary>TimeProvider, который «замораживает» паузы: таймер запрашивается и никогда не срабатывает.</summary>
    private sealed class BlockingTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _timerRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerRequested => _timerRequested.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timerRequested.TrySetResult();
            return new NoopTimer();
        }
    }

    private sealed class NoopTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
