namespace BabyMonitor.Domain.Sessions;

/// <summary>
/// Типизированная причина деградации/завершения. Потеря heartbeat и отсутствие media flow — разные причины.
/// </summary>
public enum DisconnectReason
{
    /// <summary>Причина не задана (не используется как код причины).</summary>
    None = 0,

    /// <summary>Пользователь вручную остановил мониторинг.</summary>
    ManualStop = 1,

    /// <summary>Ошибка transport-уровня (сеть, signaling, DTLS).</summary>
    TransportError = 2,

    /// <summary>Control channel перестал получать heartbeat.</summary>
    HeartbeatTimeout = 3,

    /// <summary>Control жив, но media flow отсутствует/остановился.</summary>
    MediaFlowLost = 4,

    /// <summary>Попытка соединения не удалась.</summary>
    ConnectFailed = 5,

    /// <summary>Pairing отклонён/не подтверждён.</summary>
    PairingRejected = 6,

    /// <summary>Сменилась сеть/IP; старый endpoint недействителен.</summary>
    NetworkChanged = 7,

    /// <summary>Peer закрыл соединение.</summary>
    PeerClosed = 8,

    /// <summary>Исчерпаны попытки автоматического переподключения.</summary>
    ReconnectExhausted = 9
}
