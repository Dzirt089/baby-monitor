using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// T046: ожидание TCP-подключения Child-слушателем не ограничено <see cref="SignalingOptions.HandshakeTimeout"/>
/// (Child обязан ждать Parent в Advertising); таймаут применяется только к TLS+hello уже подключившегося
/// peer-а; отмена/Dispose ожидающего Accept дают OCE/invalid_state, а не signaling_timeout.
/// </summary>
public sealed class TlsSignalingServerAcceptTests
{
    [Fact]
    public async Task AcceptAsync_без_клиента_не_завершается_по_HandshakeTimeout_и_принимает_позднего_peer()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(TimeSpan.FromMilliseconds(500)),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);

        // 700 мс > HandshakeTimeout: без TCP-подключения ожидание не должно завершаться ошибкой.
        await Task.Delay(TimeSpan.FromMilliseconds(700));
        Assert.False(
            acceptTask.IsCompleted,
            "AcceptAsync не должен завершаться по HandshakeTimeout без TCP-подключения: Child ждёт Parent в Advertising.");

        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            peers.CreateParentIdentity(),
            new SignalingOptions(TimeSpan.FromSeconds(5)));
        var clientTask = client.ConnectAsync(timeout.Token);
        var acceptResult = await acceptTask;
        var clientResult = await clientTask;

        Assert.True(acceptResult.IsSuccess, "Поздний доверенный peer должен приниматься: " + acceptResult.Error?.Code);
        Assert.True(clientResult.IsSuccess);
        await clientResult.Value.DisposeAsync();
        await acceptResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task Pending_Accept_отменяется_вызывающим_токеном_как_OperationCanceledException()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(TimeSpan.FromSeconds(5)),
            timeout.Token);
        await using var _ = serverSession;
        using var cancellation = new CancellationTokenSource();
        var acceptTask = serverSession.AcceptAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(acceptTask.IsCompleted);

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await acceptTask);
    }

    [Fact]
    public async Task Pending_Accept_после_Dispose_завершается_invalid_state_не_signaling_timeout()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(TimeSpan.FromSeconds(5)),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(acceptTask.IsCompleted);

        await serverSession.DisposeAsync();
        var result = await acceptTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.InvalidState, result.Error!.Code);
        Assert.NotEqual(RealtimeTransportErrorCodes.SignalingTimeout, result.Error!.Code);
    }
}
