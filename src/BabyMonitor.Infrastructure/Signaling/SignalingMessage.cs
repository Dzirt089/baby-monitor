namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>Тип сообщения, полученного по установленному signaling-соединению.</summary>
public enum SignalingMessageKind
{
    /// <summary>SDP offer от инициатора.</summary>
    Offer = 1,

    /// <summary>SDP answer от слушателя.</summary>
    Answer = 2,

    /// <summary>Control heartbeat ping.</summary>
    Ping = 3,

    /// <summary>Ответ на heartbeat ping.</summary>
    Pong = 4,

    /// <summary>Peer корректно завершает соединение.</summary>
    Bye = 5,

    /// <summary>Типизированный отказ peer-а.</summary>
    Error = 6
}

/// <summary>
/// Сообщение установленного signaling-соединения. Для offer/answer содержит SDP, для ping/pong —
/// control payload, для error — типизированный код отказа. SDP и payload не логируются.
/// </summary>
public sealed class SignalingMessage
{
    private SignalingMessage(
        SignalingMessageKind kind,
        string? sessionDescription,
        ReadOnlyMemory<byte> controlPayload,
        SignalingWireError? wireError)
    {
        Kind = kind;
        SessionDescription = sessionDescription;
        ControlPayload = controlPayload;
        WireError = wireError;
    }

    /// <summary>Тип сообщения.</summary>
    public SignalingMessageKind Kind { get; }

    /// <summary>SDP для offer/answer; иначе <see langword="null"/>.</summary>
    public string? SessionDescription { get; }

    /// <summary>Control payload для ping/pong; иначе пустой.</summary>
    public ReadOnlyMemory<byte> ControlPayload { get; }

    /// <summary>Код отказа из кадра error; иначе <see langword="null"/>.</summary>
    internal SignalingWireError? WireError { get; }

    internal static SignalingMessage CreateOffer(string sdp) =>
        new(SignalingMessageKind.Offer, sdp, ReadOnlyMemory<byte>.Empty, null);

    internal static SignalingMessage CreateAnswer(string sdp) =>
        new(SignalingMessageKind.Answer, sdp, ReadOnlyMemory<byte>.Empty, null);

    internal static SignalingMessage CreatePing(ReadOnlyMemory<byte> payload) =>
        new(SignalingMessageKind.Ping, null, payload, null);

    internal static SignalingMessage CreatePong(ReadOnlyMemory<byte> payload) =>
        new(SignalingMessageKind.Pong, null, payload, null);

    internal static SignalingMessage CreateBye() =>
        new(SignalingMessageKind.Bye, null, ReadOnlyMemory<byte>.Empty, null);

    internal static SignalingMessage CreateError(SignalingWireError wireError) =>
        new(SignalingMessageKind.Error, null, ReadOnlyMemory<byte>.Empty, wireError);
}
