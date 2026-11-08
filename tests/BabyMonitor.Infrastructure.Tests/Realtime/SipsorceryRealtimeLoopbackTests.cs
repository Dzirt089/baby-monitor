using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Infrastructure.Audio;
using BabyMonitor.Infrastructure.Realtime;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Loopback contract/integration тесты media-адаптера SIPSorcery (ADR-0005/ADR-0007) на 127.0.0.1:
/// TLS signaling с pinning identity, WebRTC audio-only Opus/DTLS-SRTP, передача кадра от Child
/// к Parent, закрытие/освобождение ресурсов, типизированные ошибки и role-инварианты
/// (Child — отправляет, Parent — принимает).
/// </summary>
public sealed class SipsorceryRealtimeLoopbackTests
{
    [Fact]
    public async Task Loopback_устанавливает_media_сессию_и_доставляет_Opus_кадр_Child_Parent()
    {
        using var peers = new MediaTestPeers();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var loopback = await MediaTestPeers.ConnectAsync(peers, timeout.Token);

        Assert.True(loopback.ParentResult.IsSuccess, "Parent должен установить media-сессию: " + loopback.ParentResult.Error?.Code);
        Assert.True(loopback.ChildResult.IsSuccess, "Child должен принять media-сессию: " + loopback.ChildResult.Error?.Code);
        var parentSession = loopback.ParentResult.Value;
        var childSession = loopback.ChildResult.Value;
        Assert.Equal(RealtimeTransportState.Connected, parentSession.State);
        Assert.Equal(RealtimeTransportState.Connected, childSession.State);

        using var codec = new ConcentusAudioCodec();
        var pcm = CreateSineFrame();
        var encoded = new byte[codec.MaxEncodedBytes];
        var encodedBytes = codec.Encode(pcm, encoded);
        await childSession.SendAudioAsync(encoded.AsMemory(0, encodedBytes), timeout.Token);

        var received = await ReceiveFirstFrameAsync(parentSession, timeout.Token);

        Assert.Equal(encoded.AsSpan(0, encodedBytes).ToArray(), received.Payload.ToArray());

        // Parent-side декодирование принятого Opus-кадра: pipeline Child encode → DTLS-SRTP → Parent decode.
        var decoded = new byte[codec.Format.Frame20MsBytes];
        var decodedBytes = codec.Decode(received.Payload.Span, decoded);
        Assert.Equal(codec.Format.Frame20MsBytes, decodedBytes);
    }

    [Fact]
    public async Task Закрытие_переводит_сессии_в_Closed_и_завершает_перечисление()
    {
        using var peers = new MediaTestPeers();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var loopback = await MediaTestPeers.ConnectAsync(peers, timeout.Token);
        Assert.True(loopback.ParentResult.IsSuccess);
        Assert.True(loopback.ChildResult.IsSuccess);
        var parentSession = loopback.ParentResult.Value;
        var childSession = loopback.ChildResult.Value;

        var enumerateTask = Task.Run(async () =>
        {
            var count = 0;
            await foreach (var _ in parentSession.ReceiveAudioAsync(timeout.Token))
            {
                count++;
            }

            return count;
        });

        await parentSession.CloseAsync(timeout.Token);
        await childSession.CloseAsync(timeout.Token);
        var receivedCount = await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, receivedCount);
        Assert.Equal(RealtimeTransportState.Closed, parentSession.State);
        Assert.Equal(RealtimeTransportState.Closed, childSession.State);
    }

    [Fact]
    public async Task ConnectAsync_недоступный_peer_возвращает_peer_unavailable()
    {
        using var peers = new MediaTestPeers();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var unusedPort = GetUnusedPort();
        var transport = new SipsorceryRealtimeTransport(peers.CreateParentIdentity(), peers.Options);

        var result = await transport.ConnectAsync(new TransportEndpoint("127.0.0.1", unusedPort), timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.PeerUnavailable, result.Error!.Code);
    }

    [Fact]
    public async Task Listener_отклоняет_недоверенного_peer_типизированной_ошибкой()
    {
        using var peers = new MediaTestPeers(trustEachOther: false);
        // Parent доверяет Child, Child не знает Parent — отказ должен прийти от Child.
        peers.ParentTrustedStore.Trust(peers.ChildKeyStore, BabyMonitor.Domain.Devices.DeviceRole.Parent);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        await using var loopback = await MediaTestPeers.ConnectAsync(peers, timeout.Token);

        Assert.False(loopback.ParentResult.IsSuccess);
        Assert.False(loopback.ChildResult.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, loopback.ChildResult.Error!.Code);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, loopback.ParentResult.Error!.Code);
    }

    [Fact]
    public async Task SendAudio_после_закрытия_бросает_RealtimeTransportClosedException_и_пустой_кадр_отклоняется()
    {
        using var peers = new MediaTestPeers();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using var loopback = await MediaTestPeers.ConnectAsync(peers, timeout.Token);
        Assert.True(loopback.ChildResult.IsSuccess);
        var childSession = loopback.ChildResult.Value;

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await childSession.SendAudioAsync(ReadOnlyMemory<byte>.Empty, timeout.Token));

        await childSession.CloseAsync(timeout.Token);

        await Assert.ThrowsAsync<RealtimeTransportClosedException>(
            async () => await childSession.SendAudioAsync(new byte[] { 1, 2, 3 }, timeout.Token));
    }

    [Fact]
    public async Task StartAsync_с_портом_вне_диапазона_бросает_ArgumentOutOfRangeException()
    {
        using var peers = new MediaTestPeers();
        var listener = new BabyMonitor.Infrastructure.Realtime.SipsorceryRealtimeTransportListener(
            peers.CreateChildIdentity(),
            peers.Options);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            async () => await listener.StartAsync(70_000, CancellationToken.None));
    }

    [Fact]
    public async Task ConnectAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        using var peers = new MediaTestPeers();
        var transport = new SipsorceryRealtimeTransport(peers.CreateParentIdentity(), peers.Options);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await transport.ConnectAsync(new TransportEndpoint("127.0.0.1", 1), cancellation.Token));
    }

    private static async Task<ReceivedAudioFrame> ReceiveFirstFrameAsync(
        IRealtimeTransportSession session,
        CancellationToken cancellationToken)
    {
        await foreach (var frame in session.ReceiveAudioAsync(cancellationToken))
        {
            return frame;
        }

        throw new InvalidOperationException("Перечисление завершилось без аудиокадра.");
    }

    private static byte[] CreateSineFrame()
    {
        var pcm = new byte[1920];
        for (var sampleIndex = 0; sampleIndex < 960; sampleIndex++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * 440 * sampleIndex / 48_000) * 8_000);
            System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(sampleIndex * sizeof(short)), value);
        }

        return pcm;
    }

    private static int GetUnusedPort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
