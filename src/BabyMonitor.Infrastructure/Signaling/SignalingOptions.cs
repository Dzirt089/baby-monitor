namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Настройки signaling-соединения: таймаут установки и источник времени для проверки срока действия
/// эфемерных TLS-сертификатов. Значения по умолчанию рассчитаны на локальную сеть.
/// </summary>
public sealed class SignalingOptions
{
    private static readonly TimeSpan DefaultHandshakeTimeoutValue = TimeSpan.FromSeconds(10);

    /// <summary>Создаёт настройки signaling.</summary>
    /// <param name="handshakeTimeout">Таймаут TCP connect, TLS handshake и identity assertion; больше нуля.</param>
    /// <param name="timeProvider">Источник времени; по умолчанию <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">Таймаут не положителен.</exception>
    public SignalingOptions(TimeSpan? handshakeTimeout = null, TimeProvider? timeProvider = null)
    {
        var timeout = handshakeTimeout ?? DefaultHandshakeTimeoutValue;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(handshakeTimeout), timeout, "Таймаут handshake должен быть больше нуля.");
        }

        HandshakeTimeout = timeout;
        TimeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Таймаут установки signaling-соединения (TCP + TLS + hello/hello_ack).</summary>
    public TimeSpan HandshakeTimeout { get; }

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
