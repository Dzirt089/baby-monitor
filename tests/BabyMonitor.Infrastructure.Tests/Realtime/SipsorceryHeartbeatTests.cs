using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Heartbeat contract/integration тесты SIPSorcery-адаптера: периодический ping по TLS control-каналу,
/// pong → <see cref="IRealtimeTransportSession.HeartbeatReceived"/> на обеих сторонах, отсутствие
/// событий после teardown и typed realtime.heartbeat_missed при потере pong (без ожидания 15 с —
/// options с короткими интервалами).
/// </summary>
public sealed class SipsorceryHeartbeatTests
{
    [Fact]
    public async Task Loopback_heartbeat_поднимает_событие_на_обеих_сторонах_и_не_после_закрытия()
    {
        using var peers = new MediaTestPeers(
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromSeconds(2));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var loopback = await MediaTestPeers.ConnectAsync(peers, timeout.Token);
        Assert.True(loopback.ParentResult.IsSuccess, "Parent: " + loopback.ParentResult.Error?.Code);
        Assert.True(loopback.ChildResult.IsSuccess, "Child: " + loopback.ChildResult.Error?.Code);
        var parentSession = loopback.ParentResult.Value;
        var childSession = loopback.ChildResult.Value;

        var parentHeartbeats = 0;
        var childHeartbeats = 0;
        var twoHeartbeatsOnBothSides = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void CheckHeartbeatCounts()
        {
            if (Volatile.Read(ref parentHeartbeats) >= 2 && Volatile.Read(ref childHeartbeats) >= 2)
            {
                twoHeartbeatsOnBothSides.TrySetResult();
            }
        }

        parentSession.HeartbeatReceived += (_, _) =>
        {
            Interlocked.Increment(ref parentHeartbeats);
            CheckHeartbeatCounts();
        };
        childSession.HeartbeatReceived += (_, _) =>
        {
            Interlocked.Increment(ref childHeartbeats);
            CheckHeartbeatCounts();
        };

        // Минимум два heartbeat на каждой стороне: подтверждает периодичность цикла, а не одно событие.
        await twoHeartbeatsOnBothSides.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // После teardown (Close одной стороны, Dispose другой) новых heartbeat-событий быть не должно.
        await parentSession.CloseAsync(timeout.Token);
        await childSession.DisposeAsync();
        var heartbeatsAfterTeardown = 0;
        parentSession.HeartbeatReceived += (_, _) => Interlocked.Increment(ref heartbeatsAfterTeardown);
        childSession.HeartbeatReceived += (_, _) => Interlocked.Increment(ref heartbeatsAfterTeardown);

        await Task.Delay(TimeSpan.FromMilliseconds(300));

        Assert.Equal(0, heartbeatsAfterTeardown);
        Assert.True(Volatile.Read(ref parentHeartbeats) >= 2);
        Assert.True(Volatile.Read(ref childHeartbeats) >= 2);
        Assert.Equal(RealtimeTransportState.Closed, parentSession.State);
        Assert.Equal(RealtimeTransportState.Closed, childSession.State);
    }

    [Fact]
    public async Task Отсутствие_pong_даёт_Failed_с_heartbeat_missed()
    {
        using var peers = new MediaTestPeers(
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(500));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var signalingOptions = peers.Options.Signaling;

        // Child-сторона — реальная media-сессия; Parent-сторона подключена и поднимает media, но
        // не имеет control-pump (как «зависший» peer): pong на ping не приходит.
        var server = new TlsSignalingServer(peers.CreateChildIdentity(), signalingOptions);
        var serverStart = await server.StartAsync(0, timeout.Token);
        Assert.True(serverStart.IsSuccess);
        var serverSession = serverStart.Value;
        await using var _ = serverSession;

        var parentTask = Task.Run(async () =>
        {
            var client = new TlsSignalingClient(
                new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
                peers.CreateParentIdentity(),
                signalingOptions);
            var signalingResult = await client.ConnectAsync(timeout.Token);
            Assert.True(signalingResult.IsSuccess, "Parent signaling: " + signalingResult.Error?.Code);
            var connection = signalingResult.Value;
            var mediaResult = await SipsorceryMediaHandshake.CreateOfferAsync(
                connection,
                peers.Options,
                timeout.Token);
            Assert.True(mediaResult.IsSuccess, "Parent media: " + mediaResult.Error?.Code);
            return (Connection: connection, PeerConnection: mediaResult.Value);
        });

        var childSignalingResult = await serverSession.AcceptAsync(timeout.Token);
        Assert.True(childSignalingResult.IsSuccess, "Child signaling: " + childSignalingResult.Error?.Code);
        var childConnection = childSignalingResult.Value;
        var childMediaResult = await SipsorceryMediaHandshake.CreateAnswerAsync(
            childConnection,
            peers.Options,
            timeout.Token);
        Assert.True(childMediaResult.IsSuccess, "Child media: " + childMediaResult.Error?.Code);
        var parent = await parentTask;

        await using var session = new SipsorceryRealtimeTransportSession(
            childConnection,
            childMediaResult.Value,
            peers.Options);
        var failed = new TaskCompletionSource<Error?>(TaskCreationOptions.RunContinuationsAsynchronously);
        session.StateChanged += (_, args) =>
        {
            if (args.State is RealtimeTransportState.Failed)
            {
                failed.TrySetResult(args.Error);
            }
        };

        var failedError = await failed.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotNull(failedError);
        Assert.Equal(RealtimeTransportErrorCodes.HeartbeatMissed, failedError!.Code);
        Assert.Equal(RealtimeTransportState.Failed, session.State);
        Assert.Equal(RealtimeTransportErrorCodes.HeartbeatMissed, session.LastError!.Code);

        await session.DisposeAsync();
        await parent.Connection.DisposeAsync();
        parent.PeerConnection.Dispose();
    }
}
