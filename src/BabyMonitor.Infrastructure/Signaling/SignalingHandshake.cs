using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// TLS handshake и app-layer identity assertion (ADR-0007, вариант A). Обе стороны предъявляют
/// эфемерные self-signed сертификаты (шифрование канала) и подписывают долгосрочной identity
/// транскрипт с хэшами SPKI обоих TLS-сертификатов и nonce — это связывает assertion с конкретным
/// TLS-соединением. Доверие к peer определяется только pinning-ом по trusted store; владелец
/// переданного <see cref="TcpClient"/> переходит вызывающему только при успехе.
/// </summary>
internal static class SignalingHandshake
{
    private const string TlsTargetHost = "babymonitor.local";
    private const int NonceLength = 32;

    /// <summary>Инициатор (Parent): TLS client + hello/hello_ack.</summary>
    internal static async Task<Result<TlsSignalingConnection>> ConnectAsync(
        TcpClient tcpClient,
        SignalingIdentity identity,
        SignalingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tcpClient);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        EphemeralTlsCertificate? localCertificate = null;
        SslStream? sslStream = null;
        var completed = false;
        try
        {
            localCertificate = EphemeralTlsCertificate.Create(options.TimeProvider);
            // Цепочка/hostname сертификата peer не проверяются CA: он эфемерный и self-signed.
            // Доверие к identity устанавливается app-layer assertion-ом и pinning-ом по trusted
            // store (ADR-0007); валидатор проверяет только форму сертификата, SSL policy errors
            // игнорируются осознанно.
            sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) => TlsPeerCertificateValidator.IsAcceptable(
                    certificate,
                    TlsPeerCertificateValidator.ServerAuthenticationOid,
                    options.TimeProvider));

            if (!await AuthenticateAsClientAsync(sslStream, localCertificate, cancellationToken).ConfigureAwait(false))
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            if (sslStream.RemoteCertificate is not X509Certificate2 serverCertificate)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            var localIdentityResult = await identity.GetLocalIdentityAsync(cancellationToken).ConfigureAwait(false);
            if (!localIdentityResult.IsSuccess)
            {
                return Result<TlsSignalingConnection>.Failure(localIdentityResult.Error!);
            }

            var expectedPeerResult = await identity.ResolveTrustedPeerAsync(cancellationToken).ConfigureAwait(false);
            if (!expectedPeerResult.IsSuccess)
            {
                return Result<TlsSignalingConnection>.Failure(expectedPeerResult.Error!);
            }

            var initiatorNonce = RandomNumberGenerator.GetBytes(NonceLength);
            var initiatorCertificateHash = localCertificate.SubjectPublicKeyInfoSha256;
            var responderCertificateHash = TlsPeerCertificateValidator.ComputeSubjectPublicKeyInfoSha256(serverCertificate);
            var initiatorFingerprint = SignalingAssertions.FingerprintToRaw(localIdentityResult.Value);
            var transcript = SignalingAssertions.BuildInitiatorTranscript(
                options.ProtocolVersion,
                initiatorNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                initiatorFingerprint);
            var signatureResult = await identity.SignAsync(transcript, cancellationToken).ConfigureAwait(false);
            if (!signatureResult.IsSuccess)
            {
                return Result<TlsSignalingConnection>.Failure(signatureResult.Error!);
            }

            var hello = new SignalingHello(
                options.ProtocolVersion,
                initiatorNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                initiatorFingerprint,
                signatureResult.Value);
            await SignalingFrameCodec.WriteAsync(
                sslStream,
                SignalingFrameType.Hello,
                SignalingHelloCodec.EncodeHello(hello),
                cancellationToken).ConfigureAwait(false);

            var responseFrame = await SignalingFrameCodec.ReadAsync(sslStream, cancellationToken).ConfigureAwait(false);
            if (responseFrame.FrameType == SignalingFrameType.Error)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.FromWireError(SignalingProtocol.DecodeWireError(responseFrame.Payload)));
            }

            if (responseFrame.FrameType != SignalingFrameType.HelloAck)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            SignalingHelloAck helloAck;
            try
            {
                helloAck = SignalingHelloCodec.DecodeHelloAck(responseFrame.Payload);
            }
            catch (SignalingProtocolException)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            if (helloAck.ProtocolVersion != SignalingProtocol.CurrentVersion)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnsupportedVersion());
            }

            if (!CryptographicOperations.FixedTimeEquals(helloAck.InitiatorNonce, initiatorNonce)
                || !CryptographicOperations.FixedTimeEquals(helloAck.InitiatorCertificateSpkiHash, initiatorCertificateHash)
                || !CryptographicOperations.FixedTimeEquals(helloAck.ResponderCertificateSpkiHash, responderCertificateHash))
            {
                // Повтор/подмена handshake: ответ не связан с текущим TLS-соединением и nonce.
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnauthorizedPeer());
            }

            var responderTranscript = SignalingAssertions.BuildResponderTranscript(
                SignalingProtocol.CurrentVersion,
                initiatorNonce,
                helloAck.ResponderNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                helloAck.ResponderIdentityFingerprint);
            if (!SignalingAssertions.Verify(
                    expectedPeerResult.Value,
                    helloAck.ResponderIdentityFingerprint,
                    responderTranscript,
                    helloAck.Assertion))
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnauthorizedPeer());
            }

            var connection = new TlsSignalingConnection(
                tcpClient,
                sslStream,
                localCertificate,
                expectedPeerResult.Value);
            completed = true;
            return Result<TlsSignalingConnection>.Success(connection);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (SignalingConnectionClosedException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (SignalingProtocolException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (IOException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        finally
        {
            if (!completed)
            {
                sslStream?.Dispose();
                localCertificate?.Dispose();
                tcpClient.Dispose();
            }
        }
    }

    /// <summary>Слушатель (Child): TLS server + приём hello/отправка hello_ack.</summary>
    internal static async Task<Result<TlsSignalingConnection>> AcceptAsync(
        TcpClient tcpClient,
        SignalingIdentity identity,
        SignalingOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tcpClient);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        EphemeralTlsCertificate? localCertificate = null;
        SslStream? sslStream = null;
        var completed = false;
        try
        {
            localCertificate = EphemeralTlsCertificate.Create(options.TimeProvider);
            // См. комментарий в ConnectAsync: проверка цепочки/hostname заменена pinning-ом identity
            // из trusted store и assertion-ом (ADR-0007).
            sslStream = new SslStream(
                tcpClient.GetStream(),
                leaveInnerStreamOpen: false,
                (_, certificate, _, _) => TlsPeerCertificateValidator.IsAcceptable(
                    certificate,
                    TlsPeerCertificateValidator.ClientAuthenticationOid,
                    options.TimeProvider));

            if (!await AuthenticateAsServerAsync(sslStream, localCertificate, cancellationToken).ConfigureAwait(false))
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            if (sslStream.RemoteCertificate is not X509Certificate2 clientCertificate)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            SignalingFrame helloFrame;
            try
            {
                helloFrame = await SignalingFrameCodec.ReadAsync(sslStream, cancellationToken).ConfigureAwait(false);
            }
            catch (SignalingProtocolException)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.Malformed, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            if (helloFrame.FrameType != SignalingFrameType.Hello)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.Malformed, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            SignalingHello hello;
            try
            {
                hello = SignalingHelloCodec.DecodeHello(helloFrame.Payload);
            }
            catch (SignalingProtocolException)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.Malformed, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }

            if (hello.ProtocolVersion != SignalingProtocol.CurrentVersion)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.UnsupportedVersion, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnsupportedVersion());
            }

            var expectedPeerResult = await identity.ResolveTrustedPeerAsync(cancellationToken).ConfigureAwait(false);
            if (!expectedPeerResult.IsSuccess)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.UnauthorizedPeer, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(expectedPeerResult.Error!);
            }

            var initiatorCertificateHash = TlsPeerCertificateValidator.ComputeSubjectPublicKeyInfoSha256(clientCertificate);
            var responderCertificateHash = localCertificate.SubjectPublicKeyInfoSha256;
            if (!CryptographicOperations.FixedTimeEquals(hello.InitiatorCertificateSpkiHash, initiatorCertificateHash)
                || !CryptographicOperations.FixedTimeEquals(hello.ResponderCertificateSpkiHash, responderCertificateHash))
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.UnauthorizedPeer, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnauthorizedPeer());
            }

            var initiatorTranscript = SignalingAssertions.BuildInitiatorTranscript(
                hello.ProtocolVersion,
                hello.InitiatorNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                hello.InitiatorIdentityFingerprint);
            if (!SignalingAssertions.Verify(
                    expectedPeerResult.Value,
                    hello.InitiatorIdentityFingerprint,
                    initiatorTranscript,
                    hello.Assertion))
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.UnauthorizedPeer, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.UnauthorizedPeer());
            }

            var localIdentityResult = await identity.GetLocalIdentityAsync(cancellationToken).ConfigureAwait(false);
            if (!localIdentityResult.IsSuccess)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.Internal, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(localIdentityResult.Error!);
            }

            var responderNonce = RandomNumberGenerator.GetBytes(NonceLength);
            var responderFingerprint = SignalingAssertions.FingerprintToRaw(localIdentityResult.Value);
            var responderTranscript = SignalingAssertions.BuildResponderTranscript(
                SignalingProtocol.CurrentVersion,
                hello.InitiatorNonce,
                responderNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                responderFingerprint);
            var signatureResult = await identity.SignAsync(responderTranscript, cancellationToken).ConfigureAwait(false);
            if (!signatureResult.IsSuccess)
            {
                await TrySendErrorAsync(sslStream, SignalingWireError.Internal, cancellationToken).ConfigureAwait(false);
                return Result<TlsSignalingConnection>.Failure(signatureResult.Error!);
            }

            var helloAck = new SignalingHelloAck(
                SignalingProtocol.CurrentVersion,
                hello.InitiatorNonce,
                responderNonce,
                initiatorCertificateHash,
                responderCertificateHash,
                responderFingerprint,
                signatureResult.Value);
            await SignalingFrameCodec.WriteAsync(
                sslStream,
                SignalingFrameType.HelloAck,
                SignalingHelloCodec.EncodeHelloAck(helloAck),
                cancellationToken).ConfigureAwait(false);

            var connection = new TlsSignalingConnection(
                tcpClient,
                sslStream,
                localCertificate,
                expectedPeerResult.Value);
            completed = true;
            return Result<TlsSignalingConnection>.Success(connection);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthenticationException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (SignalingConnectionClosedException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (SignalingProtocolException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        catch (IOException)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
        }
        finally
        {
            if (!completed)
            {
                sslStream?.Dispose();
                localCertificate?.Dispose();
                tcpClient.Dispose();
            }
        }
    }

    private static async Task<bool> AuthenticateAsClientAsync(
        SslStream sslStream,
        EphemeralTlsCertificate localCertificate,
        CancellationToken cancellationToken)
    {
        try
        {
            await sslStream.AuthenticateAsClientAsync(
                new SslClientAuthenticationOptions
                {
                    TargetHost = TlsTargetHost,
                    ClientCertificates = new X509CertificateCollection { localCertificate.Certificate },
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AuthenticationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task<bool> AuthenticateAsServerAsync(
        SslStream sslStream,
        EphemeralTlsCertificate localCertificate,
        CancellationToken cancellationToken)
    {
        try
        {
            await sslStream.AuthenticateAsServerAsync(
                new SslServerAuthenticationOptions
                {
                    ServerCertificateContext = localCertificate.CreateCertificateContext(),
                    ClientCertificateRequired = true,
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                    AllowRenegotiation = false,
                    EncryptionPolicy = EncryptionPolicy.RequireEncryption
                },
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (AuthenticationException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static async Task TrySendErrorAsync(
        Stream stream,
        SignalingWireError wireError,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = SignalingProtocol.EncodeWireError(wireError);
            await SignalingFrameCodec.WriteAsync(stream, SignalingFrameType.Error, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Уведомление peer об отказе не влияет на типизированный результат: соединение уже
            // отклонено, а peer мог его закрыть раньше.
        }
    }

}
