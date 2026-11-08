using System.Net;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Настройки SIPSorcery media-адаптера. Значения по умолчанию рассчитаны на локальную сеть:
/// ICE host-only (без STUN/TURN), таймауты ограничивают зависание signaling/media handshake,
/// bounded-очереди ограничивают latency и память.
/// </summary>
public sealed class SipsorceryTransportOptions
{
    private static readonly TimeSpan DefaultMediaConnectTimeoutValue = TimeSpan.FromSeconds(15);
    private const int DefaultSendQueueCapacityValue = 20;
    private const int DefaultReceiveQueueCapacityValue = 8;

    /// <summary>Создаёт настройки media-адаптера.</summary>
    /// <param name="mediaConnectTimeout">Таймаут ICE/DTLS/media handshake; больше нуля.</param>
    /// <param name="sendQueueCapacity">Ёмкость bounded-очереди отправки кадров (drop-oldest).</param>
    /// <param name="receiveQueueCapacity">Ёмкость bounded-очереди приёма кадров (drop-oldest).</param>
    /// <param name="rtpBindAddress">Адрес привязки ICE/RTP; <see langword="null"/> — адрес по умолчанию (LAN).</param>
    /// <param name="signalingOptions">Настройки signaling; по умолчанию стандартные.</param>
    /// <exception cref="ArgumentOutOfRangeException">Таймаут/ёмкость вне допустимого диапазона.</exception>
    public SipsorceryTransportOptions(
        TimeSpan? mediaConnectTimeout = null,
        int sendQueueCapacity = DefaultSendQueueCapacityValue,
        int receiveQueueCapacity = DefaultReceiveQueueCapacityValue,
        IPAddress? rtpBindAddress = null,
        SignalingOptions? signalingOptions = null)
    {
        var timeout = mediaConnectTimeout ?? DefaultMediaConnectTimeoutValue;
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaConnectTimeout), timeout, "Таймаут media handshake должен быть больше нуля.");
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
    }

    /// <summary>Таймаут ICE/DTLS/media handshake.</summary>
    public TimeSpan MediaConnectTimeout { get; }

    /// <summary>Ёмкость bounded-очереди отправки (drop-oldest при переполнении).</summary>
    public int SendQueueCapacity { get; }

    /// <summary>Ёмкость bounded-очереди приёма (drop-oldest при перегрузке).</summary>
    public int ReceiveQueueCapacity { get; }

    /// <summary>Адрес привязки ICE/RTP; <see langword="null"/> — по умолчанию.</summary>
    public IPAddress? RtpBindAddress { get; }

    /// <summary>Настройки signaling-соединения (TLS/identity assertion).</summary>
    public SignalingOptions Signaling { get; }
}
