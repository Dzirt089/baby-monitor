using System.Buffers.Binary;
using System.Security.Cryptography;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Contract-тесты внутреннего формата signaling: кадры, hello/hello_ack, identity assertion.
/// Проверяют устойчивость к malformed/oversized входу и корректность привязки транскрипта.
/// </summary>
public sealed class SignalingProtocolTests
{
    [Fact]
    public void Hello_roundtrip_сохраняет_все_поля()
    {
        var hello = new SignalingHello(
            1,
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNumberGenerator.GetBytes(64));

        var decoded = SignalingHelloCodec.DecodeHello(SignalingHelloCodec.EncodeHello(hello));

        Assert.Equal(hello.ProtocolVersion, decoded.ProtocolVersion);
        Assert.Equal(hello.InitiatorNonce, decoded.InitiatorNonce);
        Assert.Equal(hello.InitiatorCertificateSpkiHash, decoded.InitiatorCertificateSpkiHash);
        Assert.Equal(hello.ResponderCertificateSpkiHash, decoded.ResponderCertificateSpkiHash);
        Assert.Equal(hello.InitiatorIdentityFingerprint, decoded.InitiatorIdentityFingerprint);
        Assert.Equal(hello.Assertion, decoded.Assertion);
    }

    [Fact]
    public void HelloAck_roundtrip_сохраняет_все_поля()
    {
        var helloAck = new SignalingHelloAck(
            1,
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNumberGenerator.GetBytes(64));

        var decoded = SignalingHelloCodec.DecodeHelloAck(SignalingHelloCodec.EncodeHelloAck(helloAck));

        Assert.Equal(helloAck.InitiatorNonce, decoded.InitiatorNonce);
        Assert.Equal(helloAck.ResponderNonce, decoded.ResponderNonce);
        Assert.Equal(helloAck.ResponderIdentityFingerprint, decoded.ResponderIdentityFingerprint);
        Assert.Equal(helloAck.Assertion, decoded.Assertion);
    }

    [Fact]
    public void Hello_с_нулевым_nonce_отклоняется_как_malformed()
    {
        var payload = SignalingHelloCodec.EncodeHello(new SignalingHello(
            1,
            new byte[32],
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNumberGenerator.GetBytes(64)));

        Assert.Throws<SignalingProtocolException>(() => SignalingHelloCodec.DecodeHello(payload));
    }

    [Fact]
    public void Обрезанный_hello_отклоняется_без_чтения_за_границами()
    {
        var payload = SignalingHelloCodec.EncodeHello(new SignalingHello(
            1,
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNonce(),
            RandomNumberGenerator.GetBytes(64)));

        Assert.Throws<SignalingProtocolException>(
            () => SignalingHelloCodec.DecodeHello(payload.AsSpan(0, payload.Length - 1)));
    }

    [Fact]
    public async Task FrameCodec_roundtrip_и_неизвестный_тип_отклоняется()
    {
        using var stream = new MemoryStream();
        await SignalingFrameCodec.WriteAsync(stream, SignalingFrameType.Ping, new byte[] { 1, 2, 3 }, CancellationToken.None);
        stream.Position = 0;
        var frame = await SignalingFrameCodec.ReadAsync(stream, CancellationToken.None);
        Assert.Equal(SignalingFrameType.Ping, frame.FrameType);
        Assert.Equal(new byte[] { 1, 2, 3 }, frame.Payload);

        using var unknownTypeStream = new MemoryStream(new byte[] { 0x7F, 0, 0, 0, 0 });
        await Assert.ThrowsAsync<SignalingProtocolException>(
            async () => await SignalingFrameCodec.ReadAsync(unknownTypeStream, CancellationToken.None));
    }

    [Fact]
    public async Task FrameCodec_отклоняет_превышение_лимита_без_выделения_буфера()
    {
        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        header[0] = (byte)SignalingFrameType.Hello;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), SignalingProtocol.MaxControlBytes + 1);
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<SignalingProtocolException>(
            async () => await SignalingFrameCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task FrameCodec_отклоняет_offer_больше_MaxSdpBytes_без_чтения_payload() =>
        await AssertOversizedSdpFrameRejectedAsync(SignalingFrameType.Offer);

    [Fact]
    public async Task FrameCodec_отклоняет_answer_больше_MaxSdpBytes_без_чтения_payload() =>
        await AssertOversizedSdpFrameRejectedAsync(SignalingFrameType.Answer);

    [Fact]
    public async Task FrameCodec_на_закрытом_потоке_бросает_SignalingConnectionClosedException()
    {
        using var stream = new MemoryStream();

        await Assert.ThrowsAsync<SignalingConnectionClosedException>(
            async () => await SignalingFrameCodec.ReadAsync(stream, CancellationToken.None));
    }

    [Fact]
    public async Task Identity_assertion_проверяется_для_обеих_кодировок_подписи()
    {
        using var p1363Keys = new FakePairingKeyStore(derSignatures: false);
        using var derKeys = new FakePairingKeyStore(derSignatures: true);
        var transcript = RandomNumberGenerator.GetBytes(128);
        var fingerprint = SignalingAssertions.FingerprintToRaw(p1363Keys.Identity);

        var p1363Signature = await SignAsync(p1363Keys, transcript);
        var derSignature = await SignAsync(derKeys, transcript);

        Assert.True(SignalingAssertions.Verify(p1363Keys.Identity, fingerprint, transcript, p1363Signature));
        Assert.True(SignalingAssertions.Verify(derKeys.Identity, SignalingAssertions.FingerprintToRaw(derKeys.Identity), transcript, derSignature));
    }

    [Fact]
    public async Task Подмена_транскрипта_ломает_assertion()
    {
        using var keys = new FakePairingKeyStore();
        var transcript = RandomNumberGenerator.GetBytes(128);
        var signature = await SignAsync(keys, transcript);
        var tamperedTranscript = (byte[])transcript.Clone();
        tamperedTranscript[0] ^= 0xFF;

        Assert.False(SignalingAssertions.Verify(
            keys.Identity,
            SignalingAssertions.FingerprintToRaw(keys.Identity),
            tamperedTranscript,
            signature));
    }

    [Fact]
    public async Task Подмена_identity_fingerprint_ломает_assertion()
    {
        using var keys = new FakePairingKeyStore();
        using var otherKeys = new FakePairingKeyStore();
        var transcript = RandomNumberGenerator.GetBytes(128);
        var signature = await SignAsync(keys, transcript);

        Assert.False(SignalingAssertions.Verify(
            keys.Identity,
            SignalingAssertions.FingerprintToRaw(otherKeys.Identity),
            transcript,
            signature));
    }

    [Fact]
    public void Транскрипт_связывает_nonce_и_хэши_сертификатов()
    {
        var nonce = RandomNonce();
        var clientHash = RandomNonce();
        var serverHash = RandomNonce();
        var fingerprint = RandomNonce();

        var first = SignalingAssertions.BuildInitiatorTranscript(1, nonce, clientHash, serverHash, fingerprint);

        Assert.Equal(first, SignalingAssertions.BuildInitiatorTranscript(1, nonce, clientHash, serverHash, fingerprint));
        Assert.NotEqual(first, SignalingAssertions.BuildInitiatorTranscript(2, nonce, clientHash, serverHash, fingerprint));
        Assert.NotEqual(first, SignalingAssertions.BuildInitiatorTranscript(1, RandomNonce(), clientHash, serverHash, fingerprint));
        Assert.NotEqual(first, SignalingAssertions.BuildInitiatorTranscript(1, nonce, RandomNonce(), serverHash, fingerprint));
    }

    [Fact]
    public void SignalingIdentity_отклоняет_некорректный_fingerprint()
    {
        using var keys = new FakePairingKeyStore();
        var store = new InMemoryTrustedDeviceStore();

        Assert.Throws<ArgumentException>(
            () => new SignalingIdentity(keys, store, "not-a-fingerprint"));
    }

    [Fact]
    public async Task SignalingIdentity_неизвестный_peer_даёт_unauthorized_peer()
    {
        using var keys = new FakePairingKeyStore();
        using var peerKeys = new FakePairingKeyStore();
        var store = new InMemoryTrustedDeviceStore();
        var identity = new SignalingIdentity(keys, store, peerKeys.FingerprintSha256);

        var result = await identity.ResolveTrustedPeerAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.UnauthorizedPeer, result.Error!.Code);
    }

    [Fact]
    public async Task Ошибка_подписи_keystore_преобразуется_в_типизированную_ошибку()
    {
        using var keys = new FakePairingKeyStore(failSigning: true);
        var store = new InMemoryTrustedDeviceStore();
        var identity = new SignalingIdentity(keys, store, "0000000000000000000000000000000000000000000000000000000000000000");

        var result = await identity.SignAsync(new byte[] { 1, 2, 3 }, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(RealtimeTransportErrorCodes.TlsFailure, result.Error!.Code);
    }

    private static async Task AssertOversizedSdpFrameRejectedAsync(SignalingFrameType frameType)
    {
        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        header[0] = (byte)frameType;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), (uint)(SignalingProtocol.MaxSdpBytes + 1));
        using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<SignalingProtocolException>(
            async () => await SignalingFrameCodec.ReadAsync(stream, CancellationToken.None));

        // Лимит проверен по заголовку до выделения/чтения payload: поток остался на конце заголовка.
        Assert.Equal(header.Length, stream.Position);
    }

    private static byte[] RandomNonce() => RandomNumberGenerator.GetBytes(32);

    private static async Task<byte[]> SignAsync(FakePairingKeyStore keys, byte[] data) =>
        (await keys.SignAsync(data, CancellationToken.None)).Value;
}
