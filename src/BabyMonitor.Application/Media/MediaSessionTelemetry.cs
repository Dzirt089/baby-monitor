namespace BabyMonitor.Application.Media;

/// <summary>
/// Immutable telemetry-агрегаты media-сеанса для диагностики и soak-тестов: счётчики кадров,
/// число переподключений, uptime и время последнего media-кадра. Не содержит аудиоданных,
/// секретов, SDP, fingerprint и IP-адресов; частые события не логируются per-frame (FR-003 007).
/// </summary>
public sealed record MediaSessionTelemetry
{
    /// <summary>Создаёт telemetry-снимок media-сеанса.</summary>
    /// <param name="sentFrames">Успешно отправленные аудиокадры (Child); не отрицательное.</param>
    /// <param name="receivedFrames">Принятые аудиокадры (Parent); не отрицательное.</param>
    /// <param name="reconnectAttempts">Число попыток автоматического переподключения; не отрицательное.</param>
    /// <param name="uptime">Время с запуска media-сеанса; не отрицательное.</param>
    /// <param name="lastMediaFrameAt">Время последнего успешного media-кадра; null, если кадров не было.</param>
    /// <exception cref="ArgumentException">Счётчики или uptime отрицательны.</exception>
    public MediaSessionTelemetry(
        long sentFrames,
        long receivedFrames,
        int reconnectAttempts,
        TimeSpan uptime,
        DateTimeOffset? lastMediaFrameAt = null)
    {
        if (sentFrames < 0)
        {
            throw new ArgumentException("Число отправленных кадров не может быть отрицательным.", nameof(sentFrames));
        }

        if (receivedFrames < 0)
        {
            throw new ArgumentException("Число принятых кадров не может быть отрицательным.", nameof(receivedFrames));
        }

        if (reconnectAttempts < 0)
        {
            throw new ArgumentException("Число переподключений не может быть отрицательным.", nameof(reconnectAttempts));
        }

        if (uptime < TimeSpan.Zero)
        {
            throw new ArgumentException("Uptime не может быть отрицательным.", nameof(uptime));
        }

        SentFrames = sentFrames;
        ReceivedFrames = receivedFrames;
        ReconnectAttempts = reconnectAttempts;
        Uptime = uptime;
        LastMediaFrameAt = lastMediaFrameAt;
    }

    /// <summary>Успешно отправленные аудиокадры (Child).</summary>
    public long SentFrames { get; }

    /// <summary>Принятые аудиокадры (Parent).</summary>
    public long ReceivedFrames { get; }

    /// <summary>Число попыток автоматического переподключения.</summary>
    public int ReconnectAttempts { get; }

    /// <summary>Время с запуска media-сеанса до момента снимка.</summary>
    public TimeSpan Uptime { get; }

    /// <summary>Время последнего успешного media-кадра; null, если кадров ещё не было.</summary>
    public DateTimeOffset? LastMediaFrameAt { get; }
}
