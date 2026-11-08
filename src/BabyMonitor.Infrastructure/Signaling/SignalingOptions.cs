namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Настройки signaling-соединения: таймауты установки/handshake, пауза после неудачного handshake
/// (rate-limit LAN flood) и источник времени для проверки срока действия эфемерных TLS-сертификатов.
/// Значения по умолчанию рассчитаны на локальную сеть.
/// </summary>
public sealed class SignalingOptions
{
    private static readonly TimeSpan DefaultHandshakeTimeoutValue = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultFailedHandshakeCooldownValue = TimeSpan.FromMilliseconds(250);

    /// <summary>Создаёт настройки signaling.</summary>
    /// <param name="handshakeTimeout">Таймаут TCP connect, TLS handshake и identity assertion; больше нуля.</param>
    /// <param name="timeProvider">Источник времени; по умолчанию <see cref="TimeProvider.System"/>.</param>
    /// <param name="failedHandshakeCooldown">
    /// Пауза после неудачного TLS/handshake на listener-е (rate-limit rogue/флуда); по умолчанию 250 мс,
    /// больше нуля. Успешный accept паузу не применяет.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">Таймаут или cooldown не положительны.</exception>
    public SignalingOptions(
        TimeSpan? handshakeTimeout = null,
        TimeProvider? timeProvider = null,
        TimeSpan? failedHandshakeCooldown = null)
    {
        var timeout = handshakeTimeout ?? DefaultHandshakeTimeoutValue;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout), timeout, "Таймаут handshake должен быть больше нуля.");
        }

        var cooldown = failedHandshakeCooldown ?? DefaultFailedHandshakeCooldownValue;
        if (cooldown <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(failedHandshakeCooldown), cooldown, "Пауза после неудачного handshake должна быть больше нуля.");
        }

        HandshakeTimeout = timeout;
        TimeProvider = timeProvider ?? TimeProvider.System;
        FailedHandshakeCooldown = cooldown;
    }

    /// <summary>Таймаут установки signaling-соединения (TCP + TLS + hello/hello_ack).</summary>
    public TimeSpan HandshakeTimeout { get; }

    /// <summary>
    /// Пауза listener-а после неудачного TLS/handshake перед возвратом ошибки вызывающему
    /// (следующий Accept не принимает rogue-подключение мгновенно). Успешный accept паузу не применяет;
    /// caller cancellation/Dispose паузу прерывают.
    /// </summary>
    public TimeSpan FailedHandshakeCooldown { get; }

    /// <summary>
    /// Источник времени для проверки срока действия TLS-сертификатов и (через
    /// <see cref="Realtime.SipsorceryTransportOptions"/>) периодического control heartbeat сессии.
    /// </summary>
    public TimeProvider TimeProvider { get; }

    /// <summary>
    /// Версия протокола, отправляемая инициатором. Production-код обязан использовать значение по
    /// умолчанию (<see cref="SignalingProtocol.CurrentVersion"/>); переопределение существует только
    /// для contract/security-тестов несовместимой версии.
    /// </summary>
    internal ushort ProtocolVersion { get; init; } = SignalingProtocol.CurrentVersion;
}
