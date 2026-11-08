using System.Net;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Настройки SIPSorcery media-адаптера. Значения по умолчанию рассчитаны на локальную сеть:
/// ICE host-only (без STUN/TURN), таймауты ограничивают зависание signaling/media handshake,
/// bounded-очереди ограничивают latency и память, heartbeat задаёт измеряемый контроль
/// живости control-канала независимо от media flow.
/// </summary>
public sealed class SipsorceryTransportOptions
{
    private static readonly TimeSpan DefaultMediaConnectTimeoutValue = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultHeartbeatIntervalValue = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan DefaultHeartbeatTimeoutValue = TimeSpan.FromSeconds(15);
    private const int DefaultSendQueueCapacityValue = 20;
    private const int DefaultReceiveQueueCapacityValue = 8;

    /// <summary>Создаёт настройки media-адаптера.</summary>
    /// <param name="mediaConnectTimeout">Таймаут ICE/DTLS/media handshake; больше нуля.</param>
    /// <param name="sendQueueCapacity">Ёмкость bounded-очереди отправки кадров (drop-oldest).</param>
    /// <param name="receiveQueueCapacity">Ёмкость bounded-очереди приёма кадров (drop-oldest).</param>
    /// <param name="rtpBindAddress">Адрес привязки ICE/RTP; <see langword="null"/> — адрес по умолчанию (LAN).</param>
    /// <param name="signalingOptions">Настройки signaling; по умолчанию стандартные.</param>
    /// <param name="heartbeatInterval">Интервал отправки control heartbeat (ping); по умолчанию 5 с.</param>
    /// <param name="heartbeatTimeout">Порог отсутствия pong до отказа; по умолчанию 15 с, больше интервала.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Таймаут/ёмкость/интервал вне допустимого диапазона либо интервал не меньше таймаута.
    /// </exception>
    public SipsorceryTransportOptions(
        TimeSpan? mediaConnectTimeout = null,
        int sendQueueCapacity = DefaultSendQueueCapacityValue,
        int receiveQueueCapacity = DefaultReceiveQueueCapacityValue,
        IPAddress? rtpBindAddress = null,
        SignalingOptions? signalingOptions = null,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? heartbeatTimeout = null)
    {
        var timeout = mediaConnectTimeout ?? DefaultMediaConnectTimeoutValue;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaConnectTimeout), timeout, "Таймаут media handshake должен быть больше нуля.");
        }

        var heartbeatIntervalValue = heartbeatInterval ?? DefaultHeartbeatIntervalValue;
        if (heartbeatIntervalValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), heartbeatIntervalValue, "Интервал heartbeat должен быть больше нуля.");
        }

        var heartbeatTimeoutValue = heartbeatTimeout ?? DefaultHeartbeatTimeoutValue;
        if (heartbeatTimeoutValue <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout), heartbeatTimeoutValue, "Таймаут heartbeat должен быть больше нуля.");
        }

        if (heartbeatIntervalValue >= heartbeatTimeoutValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(heartbeatInterval),
                heartbeatIntervalValue,
                "Интервал heartbeat должен быть меньше таймаута heartbeat.");
        }

        if (sendQueueCapacity is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(sendQueueCapacity), sendQueueCapacity, "Ёмкость очереди отправки вне диапазона 1..1000.");
        }

        if (receiveQueueCapacity is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(receiveQueueCapacity), receiveQueueCapacity, "Ёмкость очереди приёма вне диапазона 1..1000.");
        }

        MediaConnectTimeout = timeout;
        SendQueueCapacity = sendQueueCapacity;
        ReceiveQueueCapacity = receiveQueueCapacity;
        RtpBindAddress = rtpBindAddress;
        Signaling = signalingOptions ?? new SignalingOptions();
        HeartbeatInterval = heartbeatIntervalValue;
        HeartbeatTimeout = heartbeatTimeoutValue;
    }

    /// <summary>Таймаут ICE/DTLS/media handshake.</summary>
    public TimeSpan MediaConnectTimeout { get; }

    /// <summary>Ёмкость bounded-очереди отправки (drop-oldest при переполнении).</summary>
    public int SendQueueCapacity { get; }

    /// <summary>Ёмкость bounded-очереди приёма (drop-oldest при перегрузке).</summary>
    public int ReceiveQueueCapacity { get; }

    /// <summary>Адрес привязки ICE/RTP; <see langword="null"/> — по умолчанию.</summary>
    public IPAddress? RtpBindAddress { get; }

    /// <summary>
    /// Интервал отправки control heartbeat (ping) по уже установленному TLS signaling-каналу.
    /// Heartbeat идёт независимо от media flow; отсутствие pong дольше <see cref="HeartbeatTimeout"/>
    /// переводит сессию в Failed с типом realtime.heartbeat_missed.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>
    /// Порог отсутствия pong с момента последнего подтверждённого heartbeat. Больше
    /// <see cref="HeartbeatInterval"/> (проверяется конструктором).
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; }

    /// <summary>Настройки signaling-соединения (TLS/identity assertion).</summary>
    public SignalingOptions Signaling { get; }
}
