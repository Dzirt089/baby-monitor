using System.Net;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Contract/валидация настроек media-адаптера: значения heartbeat по умолчанию, границы и связь
/// HeartbeatInterval &lt; HeartbeatTimeout (без real-time ожиданий).
/// </summary>
public sealed class SipsorceryTransportOptionsTests
{
    [Fact]
    public void Heartbeat_по_умолчанию_5_и_15_секунд()
    {
        var options = new SipsorceryTransportOptions();

        Assert.Equal(TimeSpan.FromSeconds(5), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromSeconds(15), options.HeartbeatTimeout);
        Assert.True(options.HeartbeatInterval < options.HeartbeatTimeout);
    }

    [Fact]
    public void Heartbeat_можно_переопределить_для_измерений()
    {
        var options = new SipsorceryTransportOptions(
            heartbeatInterval: TimeSpan.FromMilliseconds(100),
            heartbeatTimeout: TimeSpan.FromMilliseconds(500));

        Assert.Equal(TimeSpan.FromMilliseconds(100), options.HeartbeatInterval);
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.HeartbeatTimeout);
    }

    [Fact]
    public void Интервал_heartbeat_должен_быть_больше_нуля()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(heartbeatInterval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(heartbeatInterval: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Таймаут_heartbeat_должен_быть_больше_нуля()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(heartbeatTimeout: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(heartbeatTimeout: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void Интервал_heartbeat_должен_быть_меньше_таймаута()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(
                heartbeatInterval: TimeSpan.FromSeconds(5),
                heartbeatTimeout: TimeSpan.FromSeconds(5)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SipsorceryTransportOptions(
                heartbeatInterval: TimeSpan.FromSeconds(6),
                heartbeatTimeout: TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Остальные_настройки_не_изменились()
    {
        var options = new SipsorceryTransportOptions(
            mediaConnectTimeout: TimeSpan.FromSeconds(20),
            sendQueueCapacity: 12,
            receiveQueueCapacity: 6,
            rtpBindAddress: IPAddress.Loopback,
            signalingOptions: new SignalingOptions(TimeSpan.FromSeconds(7)));

        Assert.Equal(TimeSpan.FromSeconds(20), options.MediaConnectTimeout);
        Assert.Equal(12, options.SendQueueCapacity);
        Assert.Equal(6, options.ReceiveQueueCapacity);
        Assert.Equal(IPAddress.Loopback, options.RtpBindAddress);
        Assert.Equal(TimeSpan.FromSeconds(7), options.Signaling.HandshakeTimeout);
    }
}
