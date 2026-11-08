namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Стабильные коды ошибок signaling/media (ADR-0007). Не содержат секретов/SDP/приватных IP;
/// пользовательское сообщение отделяется от технической причины.
/// </summary>
public static class RealtimeTransportErrorCodes
{
    /// <summary>Peer не найден/недоступен (discovery/resolve/connect).</summary>
    public const string PeerUnavailable = "realtime.peer_unavailable";

    /// <summary>TLS handshake не удался (включая pinning mismatch).</summary>
    public const string TlsFailure = "realtime.tls_failure";

    /// <summary>Peer не является доверенным устройством (identity не совпала с trusted store).</summary>
    public const string UnauthorizedPeer = "realtime.unauthorized_peer";

    /// <summary>Signaling не ответил в отведённое время.</summary>
    public const string SignalingTimeout = "realtime.signaling_timeout";

    /// <summary>Неподдерживаемая версия signaling/media протокола (downgrade отклонён).</summary>
    public const string UnsupportedVersion = "realtime.unsupported_version";

    /// <summary>Signaling установлен, но media path (DTLS-SRTP/RTP) не поднялся.</summary>
    public const string MediaPathAbsent = "realtime.media_path_absent";

    /// <summary>Активный media stream потерян (stalled/failed).</summary>
    public const string MediaFlowLost = "realtime.media_flow_lost";

    /// <summary>Control heartbeat (ping/pong) не поступает дольше настроенного порога.</summary>
    public const string HeartbeatMissed = "realtime.heartbeat_missed";

    /// <summary>Сеть недоступна/сменилась до завершения операции.</summary>
    public const string NetworkUnavailable = "realtime.network_unavailable";

    /// <summary>Операция отменена/сессия закрыта локально.</summary>
    public const string SessionClosed = "realtime.session_closed";

    /// <summary>Порядок операций нарушен (например, Accept до Start, повторный Accept).</summary>
    public const string InvalidState = "realtime.invalid_state";
}
