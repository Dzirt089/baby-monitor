using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using BabyMonitor.Application.Pairing;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Установленное и аутентифицированное signaling-соединение поверх TLS (ADR-0007). Обе стороны уже
/// проверили identity peer: свойство <see cref="RemoteIdentity"/> содержит pinned публичную identity
/// из trusted store. SDP/ping/bye передаются только по TLS; соединение не логирует SDP и адреса.
/// </summary>
/// <remarks>
/// Чтение сообщений предполагает одного читателя (control/медиа-цикл адаптера); отправка
/// сериализуется внутренним семафором. Класс не является потокобезопасным для конкурентного
/// закрытия и отправки: закрытие идемпотентно.
/// </remarks>
public sealed class TlsSignalingConnection : IAsyncDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly TcpClient _tcpClient;
    private readonly SslStream _sslStream;
    private readonly EphemeralTlsCertificate _localCertificate;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _receiveLock = new(1, 1);
    private int _closed;

    internal TlsSignalingConnection(
        TcpClient tcpClient,
        SslStream sslStream,
        EphemeralTlsCertificate localCertificate,
        DeviceIdentityPublicKey remoteIdentity)
    {
        ArgumentNullException.ThrowIfNull(tcpClient);
        ArgumentNullException.ThrowIfNull(sslStream);
        ArgumentNullException.ThrowIfNull(localCertificate);
        ArgumentNullException.ThrowIfNull(remoteIdentity);

        _tcpClient = tcpClient;
        _sslStream = sslStream;
        _localCertificate = localCertificate;
        RemoteIdentity = remoteIdentity;
    }

    /// <summary>Pinned публичная identity peer, проверенная по trusted store и assertion-у.</summary>
    public DeviceIdentityPublicKey RemoteIdentity { get; }

    /// <summary>Признак закрытого соединения.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) == 1;

    /// <summary>Отправляет SDP offer.</summary>
    /// <param name="sessionDescription">SDP offer.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">SDP пуст.</exception>
    /// <exception cref="SignalingConnectionClosedException">Соединение закрыто.</exception>
    public async Task SendOfferAsync(string sessionDescription, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDescription);
        await SendFrameAsync(SignalingFrameType.Offer, Encoding.UTF8.GetBytes(sessionDescription), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Отправляет SDP answer.</summary>
    /// <param name="sessionDescription">SDP answer.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">SDP пуст.</exception>
    /// <exception cref="SignalingConnectionClosedException">Соединение закрыто.</exception>
    public async Task SendAnswerAsync(string sessionDescription, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionDescription);
        await SendFrameAsync(SignalingFrameType.Answer, Encoding.UTF8.GetBytes(sessionDescription), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Отправляет control heartbeat ping.</summary>
    /// <param name="payload">Opaque control payload (не более 8 KiB); не содержит секретов.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">Payload превышает лимит control-сообщения.</exception>
    public Task SendPingAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > SignalingProtocol.MaxControlBytes)
        {
            throw new ArgumentException("Payload ping превышает лимит control-сообщения.", nameof(payload));
        }

        return SendFrameAsync(SignalingFrameType.Ping, payload, cancellationToken);
    }

    /// <summary>Отправляет pong с эхом payload ping.</summary>
    /// <param name="payload">Opaque control payload (не более 8 KiB).</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">Payload превышает лимит control-сообщения.</exception>
    public Task SendPongAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        if (payload.Length > SignalingProtocol.MaxControlBytes)
        {
            throw new ArgumentException("Payload pong превышает лимит control-сообщения.", nameof(payload));
        }

        return SendFrameAsync(SignalingFrameType.Pong, payload, cancellationToken);
    }

    /// <summary>Отправляет bye — корректное завершение signaling-соединения.</summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    public Task SendByeAsync(CancellationToken cancellationToken) =>
        SendFrameAsync(SignalingFrameType.Bye, ReadOnlyMemory<byte>.Empty, cancellationToken);

    internal Task SendErrorAsync(SignalingWireError wireError, CancellationToken cancellationToken) =>
        SendFrameAsync(SignalingFrameType.Error, SignalingProtocol.EncodeWireError(wireError), cancellationToken);

    /// <summary>
    /// Принимает одно сообщение. Завершение потока/закрытие соединения — typed
    /// <see cref="SignalingConnectionClosedException"/>; некорректный кадр — <see cref="SignalingProtocolException"/>.
    /// </summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Принятое сообщение.</returns>
    /// <exception cref="SignalingConnectionClosedException">Соединение закрыто peer-ом или локально.</exception>
    /// <exception cref="SignalingProtocolException">Нарушение формата/лимитов протокола.</exception>
    public async Task<SignalingMessage> ReceiveAsync(CancellationToken cancellationToken)
    {
        await _receiveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            var frame = await SignalingFrameCodec.ReadAsync(_sslStream, cancellationToken).ConfigureAwait(false);
            return Decode(frame);
        }
        finally
        {
            _receiveLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) == 1)
        {
            return;
        }

        try
        {
            await _sslStream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Peer мог закрыть соединение раньше: локальное освобождение ресурсов важнее flush.
        }

        _sslStream.Dispose();
        _tcpClient.Dispose();
        _localCertificate.Dispose();
    }

    private async Task SendFrameAsync(
        SignalingFrameType frameType,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfClosed();
            await SignalingFrameCodec.WriteAsync(_sslStream, frameType, payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    private static SignalingMessage Decode(SignalingFrame frame)
    {
        return frame.FrameType switch
        {
            SignalingFrameType.Offer => SignalingMessage.CreateOffer(DecodeSessionDescription(frame.Payload)),
            SignalingFrameType.Answer => SignalingMessage.CreateAnswer(DecodeSessionDescription(frame.Payload)),
            SignalingFrameType.Ping => SignalingMessage.CreatePing(frame.Payload),
            SignalingFrameType.Pong => SignalingMessage.CreatePong(frame.Payload),
            SignalingFrameType.Bye => SignalingMessage.CreateBye(),
            SignalingFrameType.Error => SignalingMessage.CreateError(SignalingProtocol.DecodeWireError(frame.Payload)),
            _ => throw new SignalingProtocolException("Неожиданный тип signaling-кадра.")
        };
    }

    private static string DecodeSessionDescription(byte[] payload)
    {
        if (payload.Length == 0)
        {
            throw new SignalingProtocolException("Пустой SDP в signaling-сообщении.");
        }

        try
        {
            return Utf8.GetString(payload);
        }
        catch (DecoderFallbackException)
        {
            throw new SignalingProtocolException("SDP не является корректным UTF-8.");
        }
    }

    private void ThrowIfClosed()
    {
        if (IsClosed)
        {
            throw new SignalingConnectionClosedException("Signaling-соединение закрыто.");
        }
    }
}
