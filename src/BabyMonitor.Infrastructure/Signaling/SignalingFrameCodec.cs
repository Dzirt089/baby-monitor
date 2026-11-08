using System.Buffers.Binary;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>Прочитанный signaling-кадр: тип и payload (владелец — вызывающий код).</summary>
internal readonly struct SignalingFrame
{
    internal SignalingFrame(SignalingFrameType frameType, byte[] payload)
    {
        FrameType = frameType;
        Payload = payload;
    }

    internal SignalingFrameType FrameType { get; }

    internal byte[] Payload { get; }
}

/// <summary>
/// Кодек кадров signaling: 1 байт типа + 4 байта длины payload (big-endian) + payload.
/// Размер проверяется по типу кадра до выделения буфера, что ограничивает память недоверенным
/// входным потоком; обрезка кадра трактуется как malformed.
/// </summary>
internal static class SignalingFrameCodec
{
    internal static async Task WriteAsync(
        Stream stream,
        SignalingFrameType frameType,
        ReadOnlyMemory<byte> payload,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var maxPayload = SignalingProtocol.GetMaxPayloadBytes(frameType);
        if (maxPayload < 0)
        {
            throw new ArgumentException("Неизвестный тип signaling-кадра.", nameof(frameType));
        }

        if (payload.Length > maxPayload)
        {
            throw new ArgumentException("Payload превышает лимит протокола.", nameof(payload));
        }

        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        header[0] = (byte)frameType;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(1), checked((uint)payload.Length));

        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        if (!payload.IsEmpty)
        {
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<SignalingFrame> ReadAsync(Stream stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[SignalingProtocol.FrameHeaderBytes];
        if (!await ReadHeaderAsync(stream, header, cancellationToken).ConfigureAwait(false))
        {
            throw new SignalingConnectionClosedException("Signaling-соединение закрыто peer-ом.");
        }

        var frameType = (SignalingFrameType)header[0];
        var payloadLength = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(1));
        var maxPayload = SignalingProtocol.GetMaxPayloadBytes(frameType);
        if (maxPayload < 0)
        {
            throw new SignalingProtocolException("Неизвестный тип signaling-кадра.");
        }

        if (payloadLength > (uint)maxPayload)
        {
            throw new SignalingProtocolException("Signaling-кадр превышает лимит протокола.");
        }

        if (payloadLength == 0)
        {
            return new SignalingFrame(frameType, Array.Empty<byte>());
        }

        var payload = new byte[payloadLength];
        var read = 0;
        while (read < payload.Length)
        {
            var chunk = await stream.ReadAsync(payload.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
            {
                throw new SignalingProtocolException("Signaling-кадр обрезан.");
            }

            read += chunk;
        }

        return new SignalingFrame(frameType, payload);
    }

    private static async Task<bool> ReadHeaderAsync(Stream stream, byte[] header, CancellationToken cancellationToken)
    {
        var read = 0;
        while (read < header.Length)
        {
            var chunk = await stream.ReadAsync(header.AsMemory(read), cancellationToken).ConfigureAwait(false);
            if (chunk == 0)
            {
                if (read == 0)
                {
                    return false;
                }

                throw new SignalingProtocolException("Signaling-кадр обрезан в заголовке.");
            }

            read += chunk;
        }

        return true;
    }
}
