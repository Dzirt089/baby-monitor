using System.Buffers.Binary;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>Кодирование сырых кадров без проверки лимитов — для негативных тестов протокола.</summary>
internal static class RawFrames
{
    public static byte[] Create(SignalingFrameType frameType, ReadOnlySpan<byte> payload)
    {
        var frame = new byte[SignalingProtocol.FrameHeaderBytes + payload.Length];
        frame[0] = (byte)frameType;
        BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(1), checked((uint)payload.Length));
        payload.CopyTo(frame.AsSpan(SignalingProtocol.FrameHeaderBytes));
        return frame;
    }
}

/// <summary>
/// Сырой TLS-клиент для негативных тестов сервера: позволяет отправить произвольные байты кадров
/// после mutual TLS с эфемерным ECDSA-сертификатом.
/// </summary>
internal sealed class RawSignalingClient : IAsyncDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly SslStream _sslStream;
    private readonly EphemeralTlsCertificate _localCertificate;

    private RawSignalingClient(TcpClient tcpClient, SslStream sslStream, EphemeralTlsCertificate localCertificate)
    {
        _tcpClient = tcpClient;
        _sslStream = sslStream;
        _localCertificate = localCertificate;
    }

    public byte[] LocalCertificateSpkiHash => _localCertificate.SubjectPublicKeyInfoSha256;

    public byte[] RemoteCertificateSpkiHash =>
        TlsPeerCertificateValidator.ComputeSubjectPublicKeyInfoSha256(
            (X509Certificate2)_sslStream.RemoteCertificate!);

    public static async Task<RawSignalingClient> ConnectAsync(int port, CancellationToken cancellationToken)
    {
        var tcpClient = new TcpClient { NoDelay = true };
        EphemeralTlsCertificate? localCertificate = null;
        try
        {
            await tcpClient.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
            localCertificate = EphemeralTlsCertificate.Create(TimeProvider.System);
            var sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) => certificate is X509Certificate2);
            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = "babymonitor.local",
                    ClientCertificates = new X509CertificateCollection { localCertificate.Certificate },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                },
                cancellationToken);
            return new RawSignalingClient(tcpClient, sslStream, localCertificate);
        }
        catch
        {
            localCertificate?.Dispose();
            tcpClient.Dispose();
            throw;
        }
    }

    public Task WriteAsync(byte[] data, CancellationToken cancellationToken) =>
        _sslStream.WriteAsync(data, cancellationToken).AsTask();

    /// <summary>
    /// Завершает hello/hello_ack от имени доверенной identity (без последующего offer): проверяет,
    /// что listener возобновляет ожидание, если аутентифицированный peer не поднял media.
    /// </summary>
    public async Task CompleteInitiatorHandshakeAsync(FakePairingKeyStore parentKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parentKeys);
        var initiatorNonce = RandomNumberGenerator.GetBytes(32);
        var clientCertificateHash = LocalCertificateSpkiHash;
        var serverCertificateHash = RemoteCertificateSpkiHash;
        var parentFingerprint = SignalingAssertions.FingerprintToRaw(parentKeys.Identity);
        var transcript = SignalingAssertions.BuildInitiatorTranscript(
            SignalingProtocol.CurrentVersion,
            initiatorNonce,
            clientCertificateHash,
            serverCertificateHash,
            parentFingerprint);
        var signatureResult = await parentKeys.SignAsync(transcript, cancellationToken);
        var hello = new SignalingHello(
            SignalingProtocol.CurrentVersion,
            initiatorNonce,
            clientCertificateHash,
            serverCertificateHash,
            parentFingerprint,
            signatureResult.Value);
        await WriteAsync(
            RawFrames.Create(SignalingFrameType.Hello, SignalingHelloCodec.EncodeHello(hello)),
            cancellationToken);
        await ReadFrameAsync(cancellationToken);
    }

    public async Task<byte[]> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        await ReadExactAsync(header, cancellationToken);
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        if (payloadLength == 0)
        {
            return Array.Empty<byte>();
        }

        var payload = new byte[payloadLength];
        await ReadExactAsync(payload, cancellationToken);
        return payload;
    }

    private async Task ReadExactAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var chunk = await _sslStream.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (chunk == 0)
            {
                throw new IOException("TLS-поток закрыт до чтения ожидаемого кадра.");
            }

            read += chunk;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _sslStream.Dispose();
        _tcpClient.Dispose();
        _localCertificate.Dispose();
        await Task.CompletedTask;
    }
}

/// <summary>
/// Rogue TLS-сервер: проходит mutual TLS, читает hello и отправляет hello_ack, подписанный
/// доверенной identity, но с подменённым эхом nonce или повреждённой подписью. Проверяет, что
/// клиент отклоняет нарушение привязки транскрипта (replay/подмена assertion).
/// </summary>
internal sealed class RogueAckServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _serveTask;
    private readonly FakePairingKeyStore _responderKeys;
    private readonly bool _tamperEchoNonce;
    private readonly bool _tamperSignature;
    private readonly ushort _helloAckProtocolVersion;

    public RogueAckServer(
        FakePairingKeyStore responderKeys,
        bool tamperEchoNonce,
        bool tamperSignature,
        ushort helloAckProtocolVersion = SignalingProtocol.CurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(responderKeys);
        _responderKeys = responderKeys;
        _tamperEchoNonce = tamperEchoNonce;
        _tamperSignature = tamperSignature;
        _helloAckProtocolVersion = helloAckProtocolVersion;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serveTask = Task.Run(ServeAsync);
    }

    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _listener.Stop();
        try
        {
            await _serveTask;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or AuthenticationException)
        {
            // Rogue-сервер завершается вместе с тестом; ожидаемые ошибки TLS не влияют на проверку.
        }

        _lifetime.Dispose();
    }

    private async Task ServeAsync()
    {
        using var localCertificate = EphemeralTlsCertificate.Create(TimeProvider.System);
        using var tcpClient = await _listener.AcceptTcpClientAsync(_lifetime.Token);
        var sslStream = new SslStream(
            tcpClient.GetStream(),
            leaveInnerStreamOpen: false,
            (_, certificate, _, _) => certificate is X509Certificate2);
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions
            {
                ServerCertificateContext = localCertificate.CreateCertificateContext(),
                ClientCertificateRequired = true,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            },
            _lifetime.Token);

        var helloFrame = await SignalingFrameCodec.ReadAsync(sslStream, _lifetime.Token);
        var hello = SignalingHelloCodec.DecodeHello(helloFrame.Payload);
        var initiatorCertificateHash = TlsPeerCertificateValidator.ComputeSubjectPublicKeyInfoSha256(
            (X509Certificate2)sslStream.RemoteCertificate!);
        var responderCertificateHash = localCertificate.SubjectPublicKeyInfoSha256;
        var responderNonce = RandomNumberGenerator.GetBytes(32);
        var responderFingerprint = SignalingAssertions.FingerprintToRaw(_responderKeys.Identity);
        var transcript = SignalingAssertions.BuildResponderTranscript(
            SignalingProtocol.CurrentVersion,
            hello.InitiatorNonce,
            responderNonce,
            initiatorCertificateHash,
            responderCertificateHash,
            responderFingerprint);
        var signature = (await _responderKeys.SignAsync(transcript, _lifetime.Token)).Value;

        var echoNonce = (byte[])hello.InitiatorNonce.Clone();
        if (_tamperEchoNonce)
        {
            echoNonce[0] ^= 0xFF;
        }

        if (_tamperSignature)
        {
            signature[^1] ^= 0xFF;
        }

        // Подпись считается по транскрипту текущей версии (как его вычислит клиент): отказ в тесте
        // с несовместимой версией должен быть вызван только полем protocolVersion в hello_ack.
        var helloAck = new SignalingHelloAck(
            _helloAckProtocolVersion,
            echoNonce,
            responderNonce,
            initiatorCertificateHash,
            responderCertificateHash,
            responderFingerprint,
            signature);
        await SignalingFrameCodec.WriteAsync(
            sslStream,
            SignalingFrameType.HelloAck,
            SignalingHelloCodec.EncodeHelloAck(helloAck),
            _lifetime.Token);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), _lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Клиент уже отклонил handshake и закрыл соединение.
        }

        sslStream.Dispose();
    }
}

/// <summary>TLS-сервер с сертификатом чужого типа (RSA): проверяет отказ TLS-слоя до assertion.</summary>
internal sealed class ForeignCertificateServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _serveTask;

    public ForeignCertificateServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _serveTask = Task.Run(ServeAsync);
    }

    public int BoundPort => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _listener.Stop();
        try
        {
            await _serveTask;
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or SocketException or AuthenticationException)
        {
            // Ожидаемый отказ TLS: клиент отклоняет сертификат чужого типа.
        }

        _lifetime.Dispose();
    }

    private async Task ServeAsync()
    {
        using var certificate = CreateRsaCertificate();
        using var tcpClient = await _listener.AcceptTcpClientAsync(_lifetime.Token);
        var sslStream = new SslStream(tcpClient.GetStream(), leaveInnerStreamOpen: false);
        await sslStream.AuthenticateAsServerAsync(
            new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            },
            _lifetime.Token);
        sslStream.Dispose();
    }

    private static X509Certificate2 CreateRsaCertificate()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=rogue-rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection { new(TlsPeerCertificateValidator.ServerAuthenticationOid) },
            critical: false));
        var certificate = request.CreateSelfSigned(
            TimeProvider.System.GetUtcNow().AddMinutes(-5),
            TimeProvider.System.GetUtcNow().AddHours(1));
        if (!OperatingSystem.IsWindows())
        {
            return certificate;
        }

        var pfx = certificate.Export(X509ContentType.Pfx);
        certificate.Dispose();
        try
        {
            return X509CertificateLoader.LoadPkcs12(pfx, password: null, X509KeyStorageFlags.DefaultKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pfx);
        }
    }
}
