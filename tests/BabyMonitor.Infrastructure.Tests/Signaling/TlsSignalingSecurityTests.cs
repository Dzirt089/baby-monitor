using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Негативные security-тесты signaling: malformed/oversized кадры, несовместимая версия,
/// подмена assertion/nonce, чужой TLS-сертификат, таймаут handshake.
/// </summary>
public sealed class TlsSignalingSecurityTests
{
    [Fact]
    public async Task Сервер_отклоняет_неизвестный_тип_кадра()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await using var rawClient = await RawSignalingClient.ConnectAsync(serverSession.BoundPort, timeout.Token);

        await rawClient.WriteAsync(new byte[] { 0x7F, 0, 0, 0, 0 }, timeout.Token);
        var result = await acceptTask;

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Сервер_отклоняет_превышение_лимита_control_кадра()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await using var rawClient = await RawSignalingClient.ConnectAsync(serverSession.BoundPort, timeout.Token);

        var oversizedHeader = new byte[SignalingProtocol.FrameHeaderBytes];
        oversizedHeader[0] = (byte)SignalingFrameType.Hello;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            oversizedHeader.AsSpan(1),
            (uint)(SignalingProtocol.MaxControlBytes + 1));
        await rawClient.WriteAsync(oversizedHeader, timeout.Token);
        var result = await acceptTask;

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Сервер_до_проверки_identity_отклоняет_большой_SDP_кадр()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await using var rawClient = await RawSignalingClient.ConnectAsync(serverSession.BoundPort, timeout.Token);

        // До hello/hello_ack допустим только control-размер: даже если тип кадра Offer разрешает
        // 256 KiB SDP, pre-auth чтение обязано отклонить большой payload по лимиту 8 KiB.
        var oversizedOfferHeader = new byte[SignalingProtocol.FrameHeaderBytes];
        oversizedOfferHeader[0] = (byte)SignalingFrameType.Offer;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(
            oversizedOfferHeader.AsSpan(1),
            (uint)(SignalingProtocol.MaxControlBytes + 1));
        await rawClient.WriteAsync(oversizedOfferHeader, timeout.Token);
        var result = await acceptTask;

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    [Fact]
    public async Task Сервер_отклоняет_hello_с_неизвестной_версией_до_проверки_доверия()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await using var rawClient = await RawSignalingClient.ConnectAsync(serverSession.BoundPort, timeout.Token);

        var hello = new SignalingHello(
            protocolVersion: 2,
            RandomNumberGenerator.GetBytes(32),
            rawClient.LocalCertificateSpkiHash,
            rawClient.RemoteCertificateSpkiHash,
            SignalingAssertions.FingerprintToRaw(peers.ParentKeyStore.Identity),
            RandomNumberGenerator.GetBytes(64));
        await rawClient.WriteAsync(
            RawFrames.Create(SignalingFrameType.Hello, SignalingHelloCodec.EncodeHello(hello)),
            timeout.Token);
        var result = await acceptTask;

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnsupportedVersion, result.Error!.Code);
    }

    [Fact]
    public async Task Клиент_отклоняет_подменённую_подпись_assertion()
    {
        using var peers = new SignalingTestPeers();
        await using var rogueServer = new RogueAckServer(peers.ChildKeyStore, tamperEchoNonce: false, tamperSignature: true);
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", rogueServer.BoundPort),
            peers.CreateParentIdentity());

        var result = await client.ConnectAsync(timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, result.Error!.Code);
    }

    [Fact]
    public async Task Клиент_отклоняет_подменённый_эхо_nonce_replay()
    {
        using var peers = new SignalingTestPeers();
        await using var rogueServer = new RogueAckServer(peers.ChildKeyStore, tamperEchoNonce: true, tamperSignature: false);
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", rogueServer.BoundPort),
            peers.CreateParentIdentity());

        var result = await client.ConnectAsync(timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, result.Error!.Code);
    }

    [Fact]
    public async Task Клиент_отклоняет_чужой_тип_TLS_сертификата()
    {
        using var peers = new SignalingTestPeers();
        await using var foreignServer = new ForeignCertificateServer();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", foreignServer.BoundPort),
            peers.CreateParentIdentity());

        var result = await client.ConnectAsync(timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    [Fact]
    public async Task ConnectAsync_без_ответа_peer_возвращает_signaling_timeout()
    {
        using var peers = new SignalingTestPeers();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var client = new TlsSignalingClient(
                new TransportEndpoint("127.0.0.1", port),
                peers.CreateParentIdentity(),
                new SignalingOptions(TimeSpan.FromMilliseconds(300)));

            var result = await client.ConnectAsync(CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(RealtimeTransportErrorCodes.SignalingTimeout, result.Error!.Code);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task Сервер_возвращает_signaling_timeout_если_peer_не_начал_TLS()
    {
        using var peers = new SignalingTestPeers();
        var serverOptions = new SignalingOptions(TimeSpan.FromMilliseconds(300));
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            serverOptions,
            timeout.Token);
        await using var _ = serverSession;

        using var tcpClient = new TcpClient();
        await tcpClient.ConnectAsync(IPAddress.Loopback, serverSession.BoundPort, timeout.Token);
        var result = await serverSession.AcceptAsync(timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.SignalingTimeout, result.Error!.Code);
    }

    [Fact]
    public async Task Клиент_отклоняет_hello_ack_с_несовместимой_версией()
    {
        using var peers = new SignalingTestPeers();
        await using var rogueServer = new RogueAckServer(
            peers.ChildKeyStore,
            tamperEchoNonce: false,
            tamperSignature: false,
            helloAckProtocolVersion: 2);
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var client = new TlsSignalingClient(
            new TransportEndpoint("127.0.0.1", rogueServer.BoundPort),
            peers.CreateParentIdentity());

        var result = await client.ConnectAsync(timeout.Token);

        // Подпись/nonce валидны, но версия hello_ack несовместима: типизированный отказ без сессии.
        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnsupportedVersion, result.Error!.Code);
    }

    [Fact]
    public async Task Сервер_после_аутентификации_отклоняет_offer_больше_MaxSdpBytes_без_чтения_payload() =>
        await AssertOversizedSdpFrameRejectedAfterAuthenticationAsync(SignalingFrameType.Offer);

    [Fact]
    public async Task Сервер_после_аутентификации_отклоняет_answer_больше_MaxSdpBytes_без_чтения_payload() =>
        await AssertOversizedSdpFrameRejectedAfterAuthenticationAsync(SignalingFrameType.Answer);

    private static async Task AssertOversizedSdpFrameRejectedAfterAuthenticationAsync(SignalingFrameType frameType)
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await using var _ = serverSession;
        var acceptTask = serverSession.AcceptAsync(timeout.Token);
        await using var rawClient = await RawSignalingClient.ConnectAsync(serverSession.BoundPort, timeout.Token);
        await rawClient.CompleteInitiatorHandshakeAsync(peers.ParentKeyStore, timeout.Token);
        var acceptResult = await acceptTask;
        Assert.True(acceptResult.IsSuccess, "Аутентификация должна пройти: " + acceptResult.Error?.Code);
        await using var connection = acceptResult.Value;

        // Отправляется только заголовок кадра: payload (MaxSdpBytes + 1) отсутствует. Если бы кодек
        // читал payload до проверки лимита, тест завис бы вместо типизированной protocol-ошибки.
        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        header[0] = (byte)frameType;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), (uint)(SignalingProtocol.MaxSdpBytes + 1));
        await rawClient.WriteAsync(header, timeout.Token);

        var receiveTask = connection.ReceiveAsync(timeout.Token);
        await Assert.ThrowsAsync<SignalingProtocolException>(
            async () => await receiveTask.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Listener_возобновляет_ожидание_после_media_отказа_аутентифицированного_peer()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var options = new SipsorceryTransportOptions(
            mediaConnectTimeout: TimeSpan.FromSeconds(20),
            rtpBindAddress: IPAddress.Loopback,
            signalingOptions: new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout));
        var listener = new SipsorceryRealtimeTransportListener(peers.CreateChildIdentity(), options);
        var listenerResult = await listener.StartAsync(0, timeout.Token);
        Assert.True(listenerResult.IsSuccess);
        await using var listenerSession = listenerResult.Value;

        // Первый peer аутентифицирован (hello/hello_ack), но закрывает соединение до SDP offer.
        var firstAcceptTask = listenerSession.AcceptAsync(timeout.Token);
        await using (var rawClient = await RawSignalingClient.ConnectAsync(listenerSession.BoundPort, timeout.Token))
        {
            await rawClient.CompleteInitiatorHandshakeAsync(peers.ParentKeyStore, timeout.Token);
        }

        var firstResult = await firstAcceptTask;
        Assert.False(firstResult.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.MediaPathAbsent, firstResult.Error!.Code);

        // Второй peer поднимает полноценную media-сессию на том же listener-е: media-отказ не
        // «расходует» ожидание, Application может повторить Accept без пересоздания listener-а.
        var transport = new SipsorceryRealtimeTransport(peers.CreateParentIdentity(), options);
        var parentTask = transport.ConnectAsync(
            new TransportEndpoint("127.0.0.1", listenerSession.BoundPort),
            timeout.Token);
        var childTask = listenerSession.AcceptAsync(timeout.Token);
        var parentResult = await parentTask;
        var childResult = await childTask;

        Assert.True(parentResult.IsSuccess, "Повторное подключение после media-отказа должно удаться: " + parentResult.Error?.Code);
        Assert.True(childResult.IsSuccess);
        await parentResult.Value.DisposeAsync();
        await childResult.Value.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_прерывает_ожидающий_accept_типизированным_invalid_state()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var listener = new SipsorceryRealtimeTransportListener(
            peers.CreateChildIdentity(),
            new SipsorceryTransportOptions(
                rtpBindAddress: IPAddress.Loopback,
                signalingOptions: new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout)));
        var listenerResult = await listener.StartAsync(0, timeout.Token);
        Assert.True(listenerResult.IsSuccess);
        var listenerSession = listenerResult.Value;

        var acceptTask = listenerSession.AcceptAsync(timeout.Token);
        await listenerSession.DisposeAsync();
        var result = await acceptTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.InvalidState, result.Error!.Code);
    }

    [Fact]
    public async Task Dispose_останавливает_listener_и_повторный_accept_возвращает_invalid_state()
    {
        using var peers = new SignalingTestPeers();
        using var timeout = SignalingLoopbackHarness.CreateTimeout();
        var serverSession = await SignalingLoopbackHarness.StartServerAsync(
            peers.CreateChildIdentity(),
            new SignalingOptions(SignalingLoopbackHarness.DefaultTimeout),
            timeout.Token);
        await serverSession.DisposeAsync();

        var result = await serverSession.AcceptAsync(timeout.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.InvalidState, result.Error!.Code);
    }
}
