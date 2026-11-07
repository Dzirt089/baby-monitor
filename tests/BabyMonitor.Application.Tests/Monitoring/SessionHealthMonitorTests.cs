using BabyMonitor.Application.Monitoring;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Application.Tests.Monitoring;

/// <summary>Health: независимые control/media проверки и role-specific capture/playback.</summary>
public sealed class SessionHealthMonitorTests
{
    private static (SessionHealthMonitor Monitor, FakeTimeProvider Time) Create()
    {
        var time = new FakeTimeProvider();
        return (new SessionHealthMonitor(time), time);
    }

    [Fact]
    public void Без_heartbeat_control_Unknown_и_media_Inactive()
    {
        var (monitor, _) = Create();

        var health = monitor.Evaluate(DeviceRole.Child);

        Assert.Equal(SessionChannelState.Unknown, health.Control);
        Assert.Equal(AudioFlowState.Inactive, health.Media);
        Assert.False(health.IsUserHearingAudio);
    }

    [Fact]
    public void Свежий_heartbeat_делает_control_Connected()
    {
        var (monitor, _) = Create();
        monitor.MarkHeartbeat();

        var health = monitor.Evaluate(DeviceRole.Parent);

        Assert.Equal(SessionChannelState.Connected, health.Control);
    }

    [Fact]
    public void Задержка_heartbeat_делает_control_Degraded_затем_Lost()
    {
        var (monitor, time) = Create();
        monitor.MarkHeartbeat();

        time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(SessionChannelState.Degraded, monitor.Evaluate(DeviceRole.Child).Control);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(SessionChannelState.Lost, monitor.Evaluate(DeviceRole.Child).Control);
    }

    [Fact]
    public void Media_flowing_при_свежем_кадре_и_stalled_при_задержке()
    {
        var (monitor, time) = Create();
        monitor.MarkMediaFrame();
        Assert.Equal(AudioFlowState.Flowing, monitor.Evaluate(DeviceRole.Child).Media);

        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(AudioFlowState.Stalled, monitor.Evaluate(DeviceRole.Child).Media);
    }

    [Fact]
    public void Control_и_media_проверяются_независимо()
    {
        var (monitor, time) = Create();
        monitor.MarkHeartbeat();

        time.Advance(TimeSpan.FromSeconds(3));

        var health = monitor.Evaluate(DeviceRole.Parent);

        Assert.Equal(SessionChannelState.Connected, health.Control);
        Assert.Equal(AudioFlowState.Inactive, health.Media);
        Assert.False(health.IsUserHearingAudio);
    }

    [Fact]
    public void Capture_только_для_Child_playback_только_для_Parent_при_media_flowing()
    {
        var (monitor, _) = Create();
        monitor.MarkHeartbeat();
        monitor.MarkMediaFrame();

        var child = monitor.Evaluate(DeviceRole.Child);
        var parent = monitor.Evaluate(DeviceRole.Parent);

        Assert.True(child.CaptureActive);
        Assert.False(child.PlaybackActive);
        Assert.True(parent.PlaybackActive);
        Assert.False(parent.CaptureActive);
    }

    [Fact]
    public void Reset_сбрасывает_измерения()
    {
        var (monitor, _) = Create();
        monitor.MarkHeartbeat();
        monitor.MarkMediaFrame();

        monitor.Reset();

        var health = monitor.Evaluate(DeviceRole.Child);
        Assert.Equal(SessionChannelState.Unknown, health.Control);
        Assert.Equal(AudioFlowState.Inactive, health.Media);
    }

    [Fact]
    public void Некорректная_роль_запрещена()
    {
        var (monitor, _) = Create();

        Assert.Throws<ArgumentException>(() => monitor.Evaluate(DeviceRole.Unknown));
    }

    [Fact]
    public void Некорректные_пороги_запрещены()
    {
        var time = new FakeTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionHealthMonitor(time, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SessionHealthMonitor(time, mediaTimeout: TimeSpan.Zero));
    }
}

/// <summary>Независимость control и media в комбинированных состояниях.</summary>
public sealed class SessionHealthIndependenceTests
{
    [Fact]
    public void Control_Lost_при_media_Flowing_не_даёт_слышимость()
    {
        var time = new FakeTimeProvider();
        var monitor = new SessionHealthMonitor(time, heartbeatTimeout: TimeSpan.FromSeconds(2), mediaTimeout: TimeSpan.FromSeconds(10));
        monitor.MarkHeartbeat();
        time.Advance(TimeSpan.FromSeconds(3));
        monitor.MarkMediaFrame();

        var health = monitor.Evaluate(DeviceRole.Parent);

        Assert.Equal(SessionChannelState.Lost, health.Control);
        Assert.Equal(AudioFlowState.Flowing, health.Media);
        Assert.False(health.IsUserHearingAudio);
    }

    [Fact]
    public void Control_Connected_при_media_Stalled_не_даёт_слышимость()
    {
        var time = new FakeTimeProvider();
        var monitor = new SessionHealthMonitor(time, heartbeatTimeout: TimeSpan.FromSeconds(30), mediaTimeout: TimeSpan.FromSeconds(2));
        monitor.MarkHeartbeat();
        monitor.MarkMediaFrame();
        time.Advance(TimeSpan.FromSeconds(3));

        var health = monitor.Evaluate(DeviceRole.Child);

        Assert.Equal(SessionChannelState.Connected, health.Control);
        Assert.Equal(AudioFlowState.Stalled, health.Media);
        Assert.False(health.IsUserHearingAudio);
    }
}
