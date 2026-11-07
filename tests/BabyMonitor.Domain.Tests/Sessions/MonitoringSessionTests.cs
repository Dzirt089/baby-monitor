using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Domain.Tests.Sessions;

/// <summary>Таблица переходов и инвариантов MonitoringSession.</summary>
public sealed class MonitoringSessionTests
{
    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private static MonitoringSession Child() => MonitoringSession.Create("session-1", DeviceRole.Child, new StubTimeProvider());

    private static MonitoringSession Parent() => MonitoringSession.Create("session-1", DeviceRole.Parent, new StubTimeProvider());

    private static MonitoringSession ChildMediaFlowing()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();
        session.MarkConnected();
        session.MarkMediaFlowing();
        return session;
    }

    [Fact]
    public void Create_задаёт_роли_и_Idle()
    {
        var child = Child();
        var parent = Parent();

        Assert.Equal(DeviceRole.Parent, child.RemoteRole);
        Assert.Equal(DeviceRole.Child, parent.RemoteRole);
        Assert.Equal(MonitoringState.Idle, child.State);
        Assert.False(child.IsTerminal);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_отклоняет_пустой_id(string id)
    {
        Assert.Throws<ArgumentException>(() => MonitoringSession.Create(id, DeviceRole.Child, new StubTimeProvider()));
    }

    [Fact]
    public void Create_отклоняет_роль_Unknown()
    {
        Assert.Throws<ArgumentException>(() => MonitoringSession.Create("s", DeviceRole.Unknown, new StubTimeProvider()));
    }

    [Fact]
    public void Child_начинает_advertising_и_не_может_discovery()
    {
        var child = Child();

        child.BeginAdvertising();
        Assert.Equal(MonitoringState.Advertising, child.State);
        Assert.Throws<InvalidSessionTransitionException>(() => child.BeginDiscovery());
    }

    [Fact]
    public void Parent_начинает_discovery_и_не_может_advertising()
    {
        var parent = Parent();

        parent.BeginDiscovery();
        Assert.Equal(MonitoringState.Discovering, parent.State);
        Assert.Throws<InvalidSessionTransitionException>(() => parent.BeginAdvertising());
    }

    [Fact]
    public void Child_happy_path_имеет_capture_без_playback()
    {
        var child = ChildMediaFlowing();

        Assert.Equal(MonitoringState.MediaFlowing, child.State);
        Assert.True(child.Health.CaptureActive);
        Assert.False(child.Health.PlaybackActive);
        Assert.Equal(AudioFlowState.Flowing, child.Health.Media);
        Assert.True(child.Health.IsUserHearingAudio);
    }

    [Fact]
    public void Parent_happy_path_имеет_playback_без_capture()
    {
        var parent = Parent();
        parent.BeginDiscovery();
        parent.EnterPairing();
        parent.BeginConnecting();
        parent.MarkConnected();
        parent.MarkMediaFlowing();

        Assert.True(parent.Health.PlaybackActive);
        Assert.False(parent.Health.CaptureActive);
    }

    [Fact]
    public void MarkMediaFlowing_из_Idle_запрещён()
    {
        Assert.Throws<InvalidSessionTransitionException>(() => Child().MarkMediaFlowing());
    }

    [Fact]
    public void MarkConnected_из_Pairing_запрещён()
    {
        var child = Child();
        child.BeginAdvertising();
        child.EnterPairing();

        Assert.Throws<InvalidSessionTransitionException>(() => child.MarkConnected());
    }

    [Fact]
    public void MarkDegraded_требует_конкретную_причину()
    {
        var session = ChildMediaFlowing();

        Assert.Throws<ArgumentException>(() => session.MarkDegraded(DisconnectReason.None));
        Assert.Throws<ArgumentException>(() => session.MarkDegraded(DisconnectReason.ManualStop));
    }

    [Fact]
    public void HeartbeatTimeout_и_MediaFlowLost_разные_причины()
    {
        var heartbeat = ChildMediaFlowing();
        heartbeat.MarkDegraded(DisconnectReason.HeartbeatTimeout);
        Assert.Equal(DisconnectReason.HeartbeatTimeout, heartbeat.CurrentDisconnectReason);

        var media = ChildMediaFlowing();
        media.MarkDegraded(DisconnectReason.MediaFlowLost);
        Assert.Equal(DisconnectReason.MediaFlowLost, media.CurrentDisconnectReason);
        Assert.Equal(AudioFlowState.Stalled, media.Health.Media);
        Assert.Equal(SessionChannelState.Degraded, media.Health.Control);
    }

    [Fact]
    public void Reconnect_цикл_увеличивает_попытку_и_восстанавливает_media()
    {
        var session = ChildMediaFlowing();
        session.MarkDegraded(DisconnectReason.HeartbeatTimeout);
        session.BeginReconnecting();
        Assert.Equal(MonitoringState.Reconnecting, session.State);
        Assert.Equal(1, session.ReconnectAttempts);

        session.BeginConnecting();
        session.MarkConnected();
        session.MarkMediaFlowing();

        Assert.Equal(MonitoringState.MediaFlowing, session.State);
        Assert.Null(session.CurrentDisconnectReason);
        Assert.Equal(0, session.ReconnectAttempts);
    }

    [Fact]
    public void Reconnect_failed_терминален()
    {
        var session = ChildMediaFlowing();
        session.MarkDegraded(DisconnectReason.TransportError);
        session.BeginReconnecting();

        session.MarkReconnectFailed(DisconnectReason.ReconnectExhausted);

        Assert.Equal(MonitoringState.Failed, session.State);
        Assert.True(session.IsTerminal);
        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginStopping());
        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginAdvertising());
    }

    [Fact]
    public void MarkFailed_из_Connecting_с_ConnectFailed()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();

        session.MarkFailed(DisconnectReason.ConnectFailed);

        Assert.Equal(MonitoringState.Failed, session.State);
        Assert.Equal(DisconnectReason.ConnectFailed, session.CurrentDisconnectReason);
    }

    [Fact]
    public void MarkFailed_требует_конкретную_причину()
    {
        var session = Child();

        Assert.Throws<ArgumentException>(() => session.MarkFailed(DisconnectReason.None));
        Assert.Throws<ArgumentException>(() => session.MarkFailed(DisconnectReason.ManualStop));
    }

    [Fact]
    public void Ручная_остановка_завершает_сеанс_и_запрещает_повторный_старт()
    {
        var session = ChildMediaFlowing();

        session.BeginStopping();
        Assert.Equal(MonitoringState.Stopping, session.State);
        Assert.Equal(DisconnectReason.ManualStop, session.CurrentDisconnectReason);

        session.MarkStopped();
        Assert.Equal(MonitoringState.Idle, session.State);
        Assert.True(session.IsTerminal);
        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginAdvertising());
        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginStopping());
    }

    [Fact]
    public void ApplyHealth_запрещает_playback_для_Child()
    {
        var session = ChildMediaFlowing();
        var health = new SessionHealth(SessionChannelState.Connected, AudioFlowState.Flowing, true, true);

        Assert.Throws<ArgumentException>(() => session.ApplyHealth(health));
    }

    [Fact]
    public void ApplyHealth_запрещает_capture_для_Parent()
    {
        var parent = Parent();
        parent.BeginDiscovery();
        parent.EnterPairing();
        parent.BeginConnecting();
        parent.MarkConnected();
        parent.MarkMediaFlowing();
        var health = new SessionHealth(SessionChannelState.Connected, AudioFlowState.Flowing, true, true);

        Assert.Throws<ArgumentException>(() => parent.ApplyHealth(health));
    }

    [Fact]
    public void ApplyHealth_запрещён_в_Idle()
    {
        Assert.Throws<InvalidSessionTransitionException>(
            () => Child().ApplyHealth(new SessionHealth(SessionChannelState.Connected, AudioFlowState.Flowing, true, false)));
    }
}

/// <summary>Дополнительная таблица запрещённых переходов и ручной Stop во время reconnect.</summary>
public sealed class MonitoringSessionTransitionTableTests
{
    private sealed class StubTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    }

    private static MonitoringSession Child() => MonitoringSession.Create("s", DeviceRole.Child, new StubTimeProvider());

    private static MonitoringSession ChildReconnecting()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();
        session.MarkConnected();
        session.MarkMediaFlowing();
        session.MarkDegraded(DisconnectReason.HeartbeatTimeout);
        session.BeginReconnecting();
        return session;
    }

    [Fact]
    public void Ручной_stop_во_время_reconnect_отменяет_цикл()
    {
        var session = ChildReconnecting();

        session.BeginStopping();
        session.MarkStopped();

        Assert.Equal(MonitoringState.Idle, session.State);
        Assert.True(session.IsTerminal);
        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginConnecting());
    }

    [Fact]
    public void BeginStopping_из_Idle_запрещён()
    {
        Assert.Throws<InvalidSessionTransitionException>(() => Child().BeginStopping());
    }

    [Fact]
    public void MarkStopped_вне_Stopping_запрещён()
    {
        Assert.Throws<InvalidSessionTransitionException>(() => Child().MarkStopped());
    }

    [Fact]
    public void EnterPairing_из_Idle_запрещён()
    {
        Assert.Throws<InvalidSessionTransitionException>(() => Child().EnterPairing());
    }

    [Fact]
    public void BeginReconnecting_из_MediaFlowing_запрещён()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();
        session.MarkConnected();
        session.MarkMediaFlowing();

        Assert.Throws<InvalidSessionTransitionException>(() => session.BeginReconnecting());
    }

    [Fact]
    public void MarkDegraded_из_Connecting_запрещён()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();

        Assert.Throws<InvalidSessionTransitionException>(() => session.MarkDegraded(DisconnectReason.TransportError));
    }

    [Fact]
    public void MarkDegraded_отклоняет_причину_не_деградации()
    {
        var session = Child();
        session.BeginAdvertising();
        session.EnterPairing();
        session.BeginConnecting();
        session.MarkConnected();

        Assert.Throws<ArgumentException>(() => session.MarkDegraded(DisconnectReason.ConnectFailed));
        Assert.Throws<ArgumentException>(() => session.MarkDegraded(DisconnectReason.ReconnectExhausted));
    }

    [Fact]
    public void MarkReconnectFailed_отклоняет_некорректную_причину()
    {
        var session = ChildReconnecting();

        Assert.Throws<ArgumentException>(() => session.MarkReconnectFailed(DisconnectReason.HeartbeatTimeout));
        Assert.Throws<ArgumentException>(() => session.MarkReconnectFailed(DisconnectReason.ManualStop));
    }

    [Fact]
    public void Failed_терминален_и_не_принимает_MarkFailed_повторно()
    {
        var session = Child();
        session.MarkFailed(DisconnectReason.TransportError);

        Assert.Throws<InvalidSessionTransitionException>(() => session.MarkFailed(DisconnectReason.TransportError));
        Assert.Throws<InvalidSessionTransitionException>(() => session.MarkStopped());
    }

    [Fact]
    public void Reconnect_после_восстановления_снова_считает_попытки_с_единицы()
    {
        var session = ChildReconnecting();
        session.BeginConnecting();
        session.MarkConnected();
        session.MarkMediaFlowing();
        Assert.Equal(0, session.ReconnectAttempts);

        session.MarkDegraded(DisconnectReason.MediaFlowLost);
        session.BeginReconnecting();

        Assert.Equal(1, session.ReconnectAttempts);
    }
}
