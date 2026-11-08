using System.Buffers.Binary;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Константы и лимиты версионированного signaling-протокола (ADR-0007). Протокол работает только
/// поверх TLS и состоит из кадров: 1 байт типа, 4 байта длины payload (big-endian), payload.
/// Лимиты ограничивают память и защищают от DoS до проверки доверия peer; размеры проверяются
/// до выделения буфера payload.
/// </summary>
internal static class SignalingProtocol
{
    /// <summary>Единственная поддерживаемая версия протокола; неизвестная версия отклоняется без SDP/media.</summary>
    internal const ushort CurrentVersion = 1;

    /// <summary>Размер заголовка кадра: тип (1 байт) и длина payload (4 байта).</summary>
    internal const int FrameHeaderBytes = 5;

    /// <summary>Максимальный размер SDP в сообщениях offer/answer.</summary>
    internal const int MaxSdpBytes = 256 * 1024;

    /// <summary>Максимальный размер control-сообщений (hello, hello_ack, ping, pong, bye, error).</summary>
    internal const int MaxControlBytes = 8 * 1024;

    /// <summary>Возвращает лимит payload для типа кадра либо -1 для неизвестного типа.</summary>
    internal static int GetMaxPayloadBytes(SignalingFrameType frameType) => frameType switch
    {
        SignalingFrameType.Hello => MaxControlBytes,
        SignalingFrameType.HelloAck => MaxControlBytes,
        SignalingFrameType.Offer => MaxSdpBytes,
        SignalingFrameType.Answer => MaxSdpBytes,
        SignalingFrameType.Ping => MaxControlBytes,
        SignalingFrameType.Pong => MaxControlBytes,
        SignalingFrameType.Bye => MaxControlBytes,
        SignalingFrameType.Error => MaxControlBytes,
        _ => -1
    };

    /// <summary>Декодирует код отказа из payload кадра error; неизвестный код — malformed.</summary>
    internal static SignalingWireError DecodeWireError(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < sizeof(ushort))
        {
            return SignalingWireError.Malformed;
        }

        var value = BinaryPrimitives.ReadUInt16BigEndian(payload);
        return Enum.IsDefined(typeof(SignalingWireError), value)
            ? (SignalingWireError)value
            : SignalingWireError.Malformed;
    }

    /// <summary>Кодирует код отказа в payload кадра error (2 байта big-endian).</summary>
    internal static byte[] EncodeWireError(SignalingWireError wireError)
    {
        var payload = new byte[sizeof(ushort)];
        BinaryPrimitives.WriteUInt16BigEndian(payload, (ushort)wireError);
        return payload;
    }
}

/// <summary>Тип signaling-кадра. Значение фиксировано протоколом версии 1 и не меняется без новой версии.</summary>
internal enum SignalingFrameType : byte
{
    /// <summary>hello инициатора: версия, nonce, хэши SPKI TLS-сертификатов обеих сторон, identity assertion.</summary>
    Hello = 1,

    /// <summary>ответ слушателя: версия, эхо nonce инициатора, собственный nonce, хэши сертификатов, identity assertion.</summary>
    HelloAck = 2,

    /// <summary>SDP offer (UTF-8, не более <see cref="SignalingProtocol.MaxSdpBytes"/>).</summary>
    Offer = 3,

    /// <summary>SDP answer (UTF-8, не более <see cref="SignalingProtocol.MaxSdpBytes"/>).</summary>
    Answer = 4,

    /// <summary>control heartbeat; peer обязан ответить pong с тем же payload.</summary>
    Ping = 5,

    /// <summary>ответ на ping с эхом payload.</summary>
    Pong = 6,

    /// <summary>корректное завершение signaling-соединения.</summary>
    Bye = 7,

    /// <summary>типизированный отказ (не содержит секретов, SDP и адресов).</summary>
    Error = 8
}

/// <summary>Коды отказа, передаваемые в кадре error; кодируются двумя байтами (big-endian).</summary>
internal enum SignalingWireError : ushort
{
    /// <summary>Версия протокола peer не поддерживается (downgrade отклонён).</summary>
    UnsupportedVersion = 1,

    /// <summary>Identity peer не прошла проверку pinning/подписи.</summary>
    UnauthorizedPeer = 2,

    /// <summary>Некорректный/превышающий лимит кадр.</summary>
    Malformed = 3,

    /// <summary>Внутренняя ошибка стороны, отправляющей отказ.</summary>
    Internal = 4
}
