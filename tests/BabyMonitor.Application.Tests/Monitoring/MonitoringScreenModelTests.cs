using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Monitoring;

/// <summary>ViewModel экрана мониторинга с fake IForegroundSessionHost (без Android).</summary>
public sealed class MonitoringScreenModelTests
{
    private sealed class FakeHost : IForegroundSessionHost
    {
        public ForegroundSessionSnapshot? Current { get; set; }

        public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

        public Result<ForegroundSessionSnapshot> StartResult { get; set; } = Result<ForegroundSessionSnapshot>.Failure(
            new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Нет сервиса."));

        public Result<ForegroundSessionSnapshot> StopResult { get; set; } = Result<ForegroundSessionSnapshot>.Failure(
            new Error(ForegroundSessionErrorCodes.NotRunning, "Не запущено."));

        public Result<ForegroundSessionSnapshot> SnapshotResult { get; set; } = Result<ForegroundSessionSnapshot>.Failure(
            new Error(ForegroundSessionErrorCodes.NotRunning, "Не запущено."));

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken)
        {
            StartCalls++;
            return Task.FromResult(StartResult);
        }

        public Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken)
        {
            StopCalls++;
            return Task.FromResult(StopResult);
        }

        public Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SnapshotResult);

        public void Raise(ForegroundSessionSnapshot snapshot)
        {
            Current = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private static ForegroundSessionSnapshot Snapshot(DeviceRole role, MonitoringForegroundState state) =>
        new(role, state);

    [Fact]
    public async Task Initialize_NotRunning_не_является_ошибкой()
    {
        var host = new FakeHost();
        using var model = new MonitoringScreenModel(host, DeviceRole.Child);

        await model.InitializeAsync(CancellationToken.None);

        Assert.True(model.IsInitialized);
        Assert.Null(model.ErrorCode);
        Assert.Null(model.Snapshot);
    }

    [Fact]
    public async Task Initialize_читает_текущий_snapshot_и_событие_обновляет_модель()
    {
        var host = new FakeHost { SnapshotResult = Result<ForegroundSessionSnapshot>.Success(Snapshot(DeviceRole.Child, MonitoringForegroundState.Running)) };
        using var model = new MonitoringScreenModel(host, DeviceRole.Child);

        await model.InitializeAsync(CancellationToken.None);
        Assert.Equal(MonitoringForegroundState.Running, model.Snapshot!.State);

        host.Raise(Snapshot(DeviceRole.Child, MonitoringForegroundState.Stopped));
        Assert.Equal(MonitoringForegroundState.Stopped, model.Snapshot!.State);
    }

    [Fact]
    public async Task Role_mismatch_блокирует_старт_и_помечается()
    {
        var host = new FakeHost { SnapshotResult = Result<ForegroundSessionSnapshot>.Success(Snapshot(DeviceRole.Parent, MonitoringForegroundState.Running)) };
        using var model = new MonitoringScreenModel(host, DeviceRole.Child);
        await model.InitializeAsync(CancellationToken.None);

        Assert.True(model.IsRoleMismatch);
        await model.StartAsync(CancellationToken.None);
        Assert.Equal(0, host.StartCalls);
    }

    [Fact]
    public async Task Start_успех_применяет_snapshot_и_снимает_ошибку()
    {
        var host = new FakeHost
        {
            StartResult = Result<ForegroundSessionSnapshot>.Success(Snapshot(DeviceRole.Child, MonitoringForegroundState.Running))
        };
        using var model = new MonitoringScreenModel(host, DeviceRole.Child);
        await model.InitializeAsync(CancellationToken.None);

        await model.StartAsync(CancellationToken.None);

        Assert.Equal(MonitoringForegroundState.Running, model.Snapshot!.State);
        Assert.Null(model.ErrorCode);
        Assert.Equal(1, host.StartCalls);
    }

    [Fact]
    public async Task Start_ошибка_показывает_код_и_сообщение()
    {
        var host = new FakeHost
        {
            StartResult = Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotificationPermissionDenied, "Нет уведомлений."))
        };
        using var model = new MonitoringScreenModel(host, DeviceRole.Parent);
        await model.InitializeAsync(CancellationToken.None);

        await model.StartAsync(CancellationToken.None);

        Assert.Equal(ForegroundSessionErrorCodes.NotificationPermissionDenied, model.ErrorCode);
        Assert.Equal("Нет уведомлений.", model.ErrorMessage);
    }

    [Fact]
    public async Task Stop_NotRunning_не_является_ошибкой()
    {
        var host = new FakeHost();
        using var model = new MonitoringScreenModel(host, DeviceRole.Parent);
        await model.InitializeAsync(CancellationToken.None);

        await model.StopAsync(CancellationToken.None);

        Assert.Null(model.ErrorCode);
    }

    [Fact]
    public async Task Двойной_Start_во_время_busy_не_дублируется()
    {
        var gate = new TaskCompletionSource<Result<ForegroundSessionSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new GatedHost(gate);
        using var model = new MonitoringScreenModel(host, DeviceRole.Child);
        await model.InitializeAsync(CancellationToken.None);

        var first = model.StartAsync(CancellationToken.None);
        var second = model.StartAsync(CancellationToken.None);
        gate.SetResult(Result<ForegroundSessionSnapshot>.Success(Snapshot(DeviceRole.Child, MonitoringForegroundState.Running)));
        await first;
        await second;

        Assert.Equal(1, host.StartCalls);
    }

    [Fact]
    public void Dispose_отписывается_от_host()
    {
        var host = new FakeHost();
        var model = new MonitoringScreenModel(host, DeviceRole.Child);
        var raised = 0;
        model.Changed += (_, _) => raised++;
        model.Dispose();
        var before = raised;

        host.Raise(Snapshot(DeviceRole.Child, MonitoringForegroundState.Running));

        Assert.Equal(before, raised);
    }

    private sealed class GatedHost : IForegroundSessionHost
    {
        private readonly TaskCompletionSource<Result<ForegroundSessionSnapshot>> _gate;

        public GatedHost(TaskCompletionSource<Result<ForegroundSessionSnapshot>> gate) => _gate = gate;

        public ForegroundSessionSnapshot? Current => null;

        public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged
        {
            add { }
            remove { }
        }

        public int StartCalls { get; private set; }

        public Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken)
        {
            StartCalls++;
            return _gate.Task;
        }

        public Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotRunning, "Не запущено.")));

        public Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotRunning, "Не запущено.")));
    }
}
