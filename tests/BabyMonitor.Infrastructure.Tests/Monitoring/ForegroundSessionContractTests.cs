using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Monitoring;

/// <summary>
/// Общий contract suite для IForegroundSessionHost: запуск/остановка/идемпотентность/события/
/// снимки/отмена. Не зависит от Android и реального service.
/// </summary>
public abstract class ForegroundSessionContractTests
{
    protected abstract IForegroundSessionHost CreateHost();

    [Theory]
    [InlineData(DeviceRole.Child)]
    [InlineData(DeviceRole.Parent)]
    public async Task StartAsync_запускает_сессию_с_указанной_ролью(DeviceRole role)
    {
        var host = CreateHost();

        var result = await host.StartAsync(role, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(role, result.Value.Role);
        Assert.Equal(MonitoringForegroundState.Running, result.Value.State);
        Assert.Equal(result.Value, host.Current);
    }

    [Theory]
    [InlineData(DeviceRole.Unknown)]
    public async Task StartAsync_с_некорректной_ролью_возвращает_ошибку(DeviceRole role)
    {
        var host = CreateHost();

        var result = await host.StartAsync(role, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.InvalidRole, result.Error!.Code);
    }

    [Fact]
    public async Task StartAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        var host = CreateHost();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await host.StartAsync(DeviceRole.Child, cts.Token));
    }

    [Fact]
    public async Task Повторный_StartAsync_с_той_же_ролью_идемпотентен()
    {
        var host = CreateHost();
        var first = (await host.StartAsync(DeviceRole.Child, CancellationToken.None)).Value;

        var second = await host.StartAsync(DeviceRole.Child, CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(first, second.Value);
    }

    [Fact]
    public async Task StartAsync_с_другой_ролью_возвращает_already_running()
    {
        var host = CreateHost();
        await host.StartAsync(DeviceRole.Child, CancellationToken.None);

        var result = await host.StartAsync(DeviceRole.Parent, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.AlreadyRunning, result.Error!.Code);
    }

    [Fact]
    public async Task StopAsync_переводит_сессию_в_Stopped_и_уведомляет()
    {
        var host = CreateHost();
        await host.StartAsync(DeviceRole.Child, CancellationToken.None);
        var stoppedEvents = 0;
        host.SnapshotChanged += (_, snapshot) =>
        {
            if (snapshot.State is MonitoringForegroundState.Stopped)
            {
                stoppedEvents++;
            }
        };

        var result = await host.StopAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MonitoringForegroundState.Stopped, result.Value.State);
        Assert.Equal(1, stoppedEvents);
        Assert.Equal(MonitoringForegroundState.Stopped, host.Current!.State);
    }

    [Fact]
    public async Task StopAsync_без_активной_сессии_возвращает_not_running()
    {
        var host = CreateHost();

        var result = await host.StopAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.NotRunning, result.Error!.Code);
    }

    [Fact]
    public async Task GetSnapshotAsync_до_старта_возвращает_not_running()
    {
        var host = CreateHost();

        var result = await host.GetSnapshotAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.NotRunning, result.Error!.Code);
    }

    [Fact]
    public async Task SnapshotChanged_уведомляет_о_старте_и_смене_состояния()
    {
        var host = CreateHost();
        var states = new List<MonitoringForegroundState>();
        host.SnapshotChanged += (_, snapshot) => states.Add(snapshot.State);

        await host.StartAsync(DeviceRole.Parent, CancellationToken.None);
        await host.StopAsync(CancellationToken.None);

        Assert.Contains(MonitoringForegroundState.Running, states);
        Assert.Contains(MonitoringForegroundState.Stopped, states);
    }
}
