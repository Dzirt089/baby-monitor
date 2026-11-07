using BabyMonitor.Application.Monitoring;

namespace BabyMonitor.Application.Tests.Monitoring;

/// <summary>Локальная тревога Parent поднимается по локальному времени и не зависит от Child.</summary>
public sealed class ParentLossAlarmTests
{
    private static (ParentLossAlarm Alarm, FakeTimeProvider Time) Create(TimeSpan? threshold = null)
    {
        var time = new FakeTimeProvider();
        return (new ParentLossAlarm(time, threshold ?? TimeSpan.FromSeconds(15)), time);
    }

    [Fact]
    public void До_первой_жизни_peer_тревога_не_поднимается()
    {
        var (alarm, time) = Create();
        time.Advance(TimeSpan.FromMinutes(1));

        Assert.False(alarm.Evaluate());
    }

    [Fact]
    public void После_порога_тревога_поднимается_без_ответа_Child()
    {
        var (alarm, time) = Create(TimeSpan.FromSeconds(5));
        alarm.ReportPeerAlive();

        time.Advance(TimeSpan.FromSeconds(4));
        Assert.False(alarm.Evaluate());

        time.Advance(TimeSpan.FromSeconds(2));
        Assert.True(alarm.Evaluate());
    }

    [Fact]
    public void Живой_peer_сбрасывает_тревогу()
    {
        var (alarm, time) = Create(TimeSpan.FromSeconds(5));
        alarm.ReportPeerAlive();
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.True(alarm.Evaluate());

        alarm.ReportPeerAlive();
        Assert.False(alarm.Evaluate());
    }

    [Fact]
    public void События_меняются_при_подъёме_и_сбросе()
    {
        var (alarm, time) = Create(TimeSpan.FromSeconds(5));
        var raisedStates = new List<bool>();
        alarm.AlarmChanged += (_, raised) => raisedStates.Add(raised);

        alarm.ReportPeerAlive();
        time.Advance(TimeSpan.FromSeconds(6));
        alarm.Evaluate();
        alarm.ReportPeerAlive();
        alarm.Evaluate();

        Assert.Equal([true, false], raisedStates);
    }

    [Fact]
    public void Reset_снимает_тревогу()
    {
        var (alarm, time) = Create(TimeSpan.FromSeconds(5));
        alarm.ReportPeerAlive();
        time.Advance(TimeSpan.FromSeconds(6));
        Assert.True(alarm.Evaluate());

        alarm.Reset();

        Assert.False(alarm.IsRaised);
        Assert.False(alarm.Evaluate());
    }

    [Fact]
    public void Некорректный_порог_запрещён()
    {
        var time = new FakeTimeProvider();

        Assert.Throws<ArgumentOutOfRangeException>(() => new ParentLossAlarm(time, TimeSpan.Zero));
    }
}

/// <summary>Повторная оценка без изменения состояния не должна дублировать события.</summary>
public sealed class ParentLossAlarmEventTests
{
    [Fact]
    public void Повторный_Evaluate_не_дублирует_события()
    {
        var time = new FakeTimeProvider();
        var alarm = new ParentLossAlarm(time, TimeSpan.FromSeconds(5));
        var events = new List<bool>();
        alarm.AlarmChanged += (_, raised) => events.Add(raised);
        alarm.ReportPeerAlive();
        time.Advance(TimeSpan.FromSeconds(6));

        alarm.Evaluate();
        alarm.Evaluate();
        alarm.Evaluate();

        Assert.Equal([true], events);
    }
}
