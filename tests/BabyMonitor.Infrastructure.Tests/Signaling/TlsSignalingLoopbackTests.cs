using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Security/contract тесты TLS signaling (ADR-0007): mutual TLS с эфемерными сертификатами,
/// pinning identity по trusted store, identity assertion над транскриптом, версия протокола,
/// ping/pong/bye и повторный Accept.
/// </summary>
public sealed class TlsSignalingLoopbackTests
{
    private const string SampleOfferSdp = "v=0\r\no=- 1 1 IN IP4 0.0.0.0\r\ns=-\r\nm=audio 9 UDP/TLS/RTP/SAVP 111\r\n";

    [Fact]
    public async Task Loopback_устанавливает_взаимный_TLS_и_фиксирует_identity_peer()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();

        await using var loopback = await SignalingLoopback.ConnectAsync(peers, cancellationToken: timeout.Token);

        Assert.True(loopback.ClientResult.IsSuccess, "Parent должен установить TLS signaling.");
        Assert.True(loopback.ServerResult.IsSuccess, "Child должен принять доверенного parent.");
        Assert.Equal(peers.ChildKeyStore.Identity, loopback.ClientResult.Value.RemoteIdentity);
        Assert.Equal(peers.ParentKeyStore.Identity, loopback.ServerResult.Value.RemoteIdentity);
    }

    [Fact]
    public async Task Loopback_обменивается_offer_answer_ping_pong_bye()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        await using var loopback = await SignalingLoopback.ConnectAsync(peers, cancellationToken: timeout.Token);
        Assert.True(loopback.ClientResult.IsSuccess);
        Assert.True(loopback.ServerResult.IsSuccess);
        var parent = loopback.ClientResult.Value;
        var child = loopback.ServerResult.Value;

        await parent.SendOfferAsync(SampleOfferSdp, timeout.Token);
        var receivedOffer = await child.ReceiveAsync(timeout.Token);
        Assert.Equal(SignalingMessageKind.Offer, receivedOffer.Kind);
        Assert.Equal(SampleOfferSdp, receivedOffer.SessionDescription);

        const string answerSdp = "v=0\r\no=- 2 2 IN IP4 0.0.0.0\r\ns=-\r\nm=audio 9 UDP/TLS/RTP/SAVP 111\r\n";
        await child.SendAnswerAsync(answerSdp, timeout.Token);
        var receivedAnswer = await parent.ReceiveAsync(timeout.Token);
        Assert.Equal(SignalingMessageKind.Answer, receivedAnswer.Kind);
        Assert.Equal(answerSdp, receivedAnswer.SessionDescription);

        var pingPayload = new byte[] { 1, 2, 3, 4 };
        await parent.SendPingAsync(pingPayload, timeout.Token);
        var receivedPing = await child.ReceiveAsync(timeout.Token);
        Assert.Equal(SignalingMessageKind.Ping, receivedPing.Kind);
        Assert.Equal(pingPayload, receivedPing.ControlPayload.ToArray());

        await child.SendPongAsync(receivedPing.ControlPayload, timeout.Token);
        var receivedPong = await parent.ReceiveAsync(timeout.Token);
        Assert.Equal(SignalingMessageKind.Pong, receivedPong.Kind);
        Assert.Equal(pingPayload, receivedPong.ControlPayload.ToArray());

        await parent.SendByeAsync(timeout.Token);
        var receivedBye = await child.ReceiveAsync(timeout.Token);
        Assert.Equal(SignalingMessageKind.Bye, receivedBye.Kind);
    }

    [Fact]
    public async Task Повторный_Accept_возвращает_invalid_state()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        await using var loopback = await SignalingLoopback.ConnectAsync(peers, cancellationToken: timeout.Token);
        Assert.True(loopback.ServerResult.IsSuccess);

        var secondAccept = await loopback.ServerSession.AcceptAsync(timeout.Token);

        Assert.False(secondAccept.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.InvalidState, secondAccept.Error!.Code);
    }

    [Fact]
    public async Task Недоверенный_peer_отклоняется_с_unauthorized_peer()
    {
        using var peers = new SignalingTestPeers(trustEachOther: false);
        // Parent доверяет Child (иначе отказ произойдёт локально), Child не знает Parent.
        peers.ParentTrustedStore.Trust(peers.ChildKeyStore, DeviceRole.Parent);
        using var timeout = SignalingLoopbackHarness.CreateTimeout();

        await using var loopback = await SignalingLoopback.ConnectAsync(peers, cancellationToken: timeout.Token);

        Assert.False(loopback.ServerResult.IsSuccess, "Child обязан отклонить недоверенного parent.");
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, loopback.ServerResult.Error!.Code);
        Assert.False(loopback.ClientResult.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, loopback.ClientResult.Error!.Code);
    }

    [Fact]
    public async Task Client_отклоняет_identity_не_совпавшую_с_trusted_store()
    {
        using var parentKeyStore = new FakePairingKeyStore();
        using var trustedChildKeyStore = new FakePairingKeyStore();
        using var rogueChildKeyStore = new FakePairingKeyStore();
        var parentTrustedStore = new InMemoryTrustedDeviceStore();
        parentTrustedStore.Trust(trustedChildKeyStore, DeviceRole.Parent);
        var rogueChildTrustedStore = new InMemoryTrustedDeviceStore();
        rogueChildTrustedStore.Trust(parentKeyStore, DeviceRole.Child);

        var parentIdentity = new SignalingIdentity(
            parentKeyStore,
            parentTrustedStore,
            trustedChildKeyStore.FingerprintSha256);
        var rogueChildIdentity = new SignalingIdentity(
            rogueChildKeyStore,
            rogueChildTrustedStore,
            parentKeyStore.FingerprintSha256);

        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(rogueChildIdentity, null, timeout.Token);
        await using var _ = serverSession;
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            parentIdentity);

        var clientTask = client.ConnectAsync(timeout.Token);
        var serverTask = serverSession.AcceptAsync(timeout.Token);
        var clientResult = await clientTask;
        var serverResult = await serverTask;

        Assert.False(clientResult.IsSuccess, "Parent обязан отклонить чужую identity за trusted store.");
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, clientResult.Error!.Code);
        if (serverResult.IsSuccess)
        {
            await serverResult.Value.DisposeAsync();
        }
    }

    [Fact]
    public async Task Неизвестная_версия_протокола_отклоняется_как_unsupported_version()
    {
        using var peers = new SignalingTestPeers();
        var parentOptions = new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout)
        {
            ProtocolVersion = 2
        };
        using var timeout = SignalingLoopbackHarness.CreateTimeout();

        await using var loopback = await SignalingLoopback.ConnectAsync(
            peers,
            parentOptions: parentOptions,
            childOptions: new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            cancellationToken: timeout.Token);

        Assert.False(loopback.ServerResult.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnsupportedVersion, loopback.ServerResult.Error!.Code);
        Assert.False(loopback.ClientResult.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnsupportedVersion, loopback.ClientResult.Error!.Code);
    }

    [Fact]
    public async Task ConnectAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        using var peers = new SignalingTestPeers();
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", 1),
            peers.CreateParentIdentity());
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await client.ConnectAsync(cancellation.Token));
    }

    [Fact]
    public async Task Listener_остаётся_рабочим_после_отказа_недоверенному_peer()
    {
        using var trustedParentKeyStore = new FakePairingKeyStore();
        using var childKeyStore = new FakePairingKeyStore();
        using var rogueParentKeyStore = new FakePairingKeyStore();
        var childTrustedStore = new InMemoryTrustedDeviceStore();
        childTrustedStore.Trust(trustedParentKeyStore, DeviceRole.Child);
        var rogueParentTrustedStore = new InMemoryTrustedDeviceStore();
        rogueParentTrustedStore.Trust(childKeyStore, DeviceRole.Parent);

        var childIdentity = new SignalingIdentity(childKeyStore, childTrustedStore, trustedParentKeyStore.FingerprintSha256);
        var rogueParentIdentity = new SignalingIdentity(
            rogueParentKeyStore,
            rogueParentTrustedStore,
            childKeyStore.FingerprintSha256);

        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(childIdentity, null, timeout.Token);
        await using var _ = serverSession;

        var rogueClient = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            rogueParentIdentity);
        var rogueClientTask = rogueClient.ConnectAsync(timeout.Token);
        var rogueAcceptTask = serverSession.AcceptAsync(timeout.Token);
        var rogueClientResult = await rogueClientTask;
        var rogueServerResult = await rogueAcceptTask;
        Assert.False(rogueClientResult.IsSuccess);
        Assert.False(rogueServerResult.IsSuccess);

        var parentIdentity = new SignalingIdentity(
            trustedParentKeyStore,
            rogueParentTrustedStore,
            childKeyStore.FingerprintSha256);
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", serverSession.BoundPort),
            parentIdentity);
        var clientTask = client.ConnectAsync(timeout.Token);
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        var clientResult = await clientTask;
        var serverResult = await acceptTask;

        Assert.True(clientResult.IsSuccess, "Listener должен принимать доверенного peer после отказа.");
        Assert.True(serverResult.IsSuccess);
        await clientResult.Value.DisposeAsync();
        await serverResult.Value.DisposeAsync();
    }
}
