using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Diagnostics;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Tests.Pairing;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Diagnostics;

/// <summary>
/// Модель экрана диагностики: snapshot роли/foreground/доверенных устройств, NotRunning,
/// ошибки host/store, подписка на события и корректный dispose.
/// </summary>
public sealed class DiagnosticsScreenModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTimeOffset PairedAt = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    private sealed class FakeForegroundSessionHost : IForegroundSessionHost
    {
        public ForegroundSessionSnapshot? Current { get; set; }

        public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

        public Result<ForegroundSessionSnapshot> SnapshotResult { get; set; } =
            Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotRunning, "Foreground-сессия не запущена."));

        public Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken) =>
            Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Сервис недоступен.")));

        public Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SnapshotResult);

        public Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SnapshotResult);

        public void Raise(ForegroundSessionSnapshot snapshot)
        {
            Current = snapshot;
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class DeviceSeed : IDisposable
    {
        private readonly List<FakePairingKeyStore> _keys = [];

        public PairedDevice Create(DeviceRole localRole, DateTimeOffset pairedAt)
        {
            var keys = new FakePairingKeyStore();
            _keys.Add(keys);
            return new PairedDevice(keys.Identity, localRole, pairedAt);
        }

        public void Dispose()
        {
            foreach (var keys in _keys)
            {
                keys.Dispose();
            }
        }
    }

    private sealed class FailingGetAllStore : ITrustedDeviceStore
    {
        public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            Task.FromResult(Result<PairedDevice?>.Success(null));

        public Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<IReadOnlyList<PairedDevice>>.Failure(
                new Error(PairingErrorCodes.StoreFailed, "Trusted store недоступен.")));

        public Task<Result> SaveAsync(PairedDevice device, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            Task.FromResult(Result<bool>.Success(false));
    }

    [Fact]
    public async Task Refresh_строит_snapshot_с_ролью_foreground_и_доверенными_устройствами()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        await store.SaveAsync(seed.Create(DeviceRole.Parent, PairedAt), None);
        var host = new FakeForegroundSessionHost
        {
            SnapshotResult = Result<ForegroundSessionSnapshot>.Success(
                new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Running))
        };
        using var model = new DiagnosticsScreenModel(host, store, DeviceRole.Child);

        await model.RefreshAsync(None);

        Assert.NotNull(model.Snapshot);
        Assert.Equal(DeviceRole.Child, model.Snapshot!.Role);
        Assert.NotNull(model.Snapshot.Foreground);
        Assert.Equal(MonitoringForegroundState.Running, model.Snapshot.Foreground.State);
        Assert.Single(model.Snapshot.TrustedDevices);
        Assert.Equal(DeviceRole.Child, model.Snapshot.TrustedDevices[0].LocalRole);
        Assert.Null(model.ErrorCode);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Refresh_NotRunning_даёт_foreground_null_без_ошибки()
    {
        var host = new FakeForegroundSessionHost();
        using var model = new DiagnosticsScreenModel(host, new InMemoryTrustedDeviceStore(), DeviceRole.Child);

        await model.RefreshAsync(None);

        Assert.NotNull(model.Snapshot);
        Assert.Null(model.Snapshot!.Foreground);
        Assert.Null(model.ErrorCode);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task Refresh_ошибка_host_выставляет_ошибку_и_не_обновляет_snapshot()
    {
        var host = new FakeForegroundSessionHost
        {
            SnapshotResult = Result<ForegroundSessionSnapshot>.Success(
                new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Running))
        };
        using var model = new DiagnosticsScreenModel(host, new InMemoryTrustedDeviceStore(), DeviceRole.Child);
        await model.RefreshAsync(None);
        Assert.NotNull(model.Snapshot);

        host.SnapshotResult = Result<ForegroundSessionSnapshot>.Failure(
            new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Сервис недоступен."));
        await model.RefreshAsync(None);

        Assert.Equal(ForegroundSessionErrorCodes.ServiceUnavailable, model.ErrorCode);
        Assert.NotNull(model.ErrorMessage);
        Assert.Equal(MonitoringForegroundState.Running, model.Snapshot!.Foreground!.State);
    }

    [Fact]
    public async Task Refresh_ошибка_store_выставляет_ошибку_и_не_строит_snapshot()
    {
        var host = new FakeForegroundSessionHost();
        using var model = new DiagnosticsScreenModel(host, new FailingGetAllStore(), DeviceRole.Child);

        await model.RefreshAsync(None);

        Assert.Equal(PairingErrorCodes.StoreFailed, model.ErrorCode);
        Assert.Null(model.Snapshot);
    }

    [Fact]
    public async Task Refresh_фильтрует_доверенные_устройства_по_роли()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        await store.SaveAsync(seed.Create(DeviceRole.Parent, PairedAt), None);
        var host = new FakeForegroundSessionHost();
        using var model = new DiagnosticsScreenModel(host, store, DeviceRole.Parent);

        await model.RefreshAsync(None);

        Assert.NotNull(model.Snapshot);
        Assert.Single(model.Snapshot!.TrustedDevices);
        Assert.Equal(DeviceRole.Parent, model.Snapshot.TrustedDevices[0].LocalRole);
    }

    [Fact]
    public async Task SnapshotChanged_обновляет_foreground_и_поднимает_Changed()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        var host = new FakeForegroundSessionHost
        {
            SnapshotResult = Result<ForegroundSessionSnapshot>.Success(
                new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Running))
        };
        using var model = new DiagnosticsScreenModel(host, store, DeviceRole.Child);
        await model.RefreshAsync(None);
        var raised = 0;
        model.Changed += (_, _) => raised++;

        host.Raise(new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Stopped));

        Assert.Equal(MonitoringForegroundState.Stopped, model.Snapshot!.Foreground!.State);
        Assert.Single(model.Snapshot.TrustedDevices);
        Assert.Equal(1, raised);
    }

    [Fact]
    public async Task Dispose_отписывается_от_изменений_host()
    {
        var host = new FakeForegroundSessionHost
        {
            SnapshotResult = Result<ForegroundSessionSnapshot>.Success(
                new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Running))
        };
        var model = new DiagnosticsScreenModel(host, new InMemoryTrustedDeviceStore(), DeviceRole.Child);
        await model.RefreshAsync(None);
        var raised = 0;
        model.Changed += (_, _) => raised++;

        model.Dispose();
        model.Dispose();
        host.Raise(new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Stopped));

        Assert.Equal(MonitoringForegroundState.Running, model.Snapshot!.Foreground!.State);
        Assert.Equal(0, raised);
    }

    [Fact]
    public void Ctor_проверяет_аргументы()
    {
        var host = new FakeForegroundSessionHost();
        var store = new InMemoryTrustedDeviceStore();

        Assert.Throws<ArgumentNullException>(() => new DiagnosticsScreenModel(null!, store, DeviceRole.Child));
        Assert.Throws<ArgumentNullException>(() => new DiagnosticsScreenModel(host, null!, DeviceRole.Child));
        Assert.Throws<ArgumentException>(() => new DiagnosticsScreenModel(host, store, DeviceRole.Unknown));
    }

    [Fact]
    public void DiagnosticsSnapshot_копирует_коллекцию_и_проверяет_аргументы()
    {
        using var seed = new DeviceSeed();
        var device = seed.Create(DeviceRole.Child, PairedAt);
        var source = new List<PairedDeviceSummary> { PairedDeviceSummary.From(device) };

        var snapshot = new DiagnosticsSnapshot(DeviceRole.Child, null, source);
        source.Clear();

        Assert.Single(snapshot.TrustedDevices);
        Assert.Null(snapshot.Foreground);
        Assert.Equal(DeviceRole.Child, snapshot.Role);
        Assert.Throws<ArgumentNullException>(
            () => new DiagnosticsSnapshot(DeviceRole.Child, null, null!));
        Assert.Throws<ArgumentException>(
            () => new DiagnosticsSnapshot(DeviceRole.Unknown, null, Array.Empty<PairedDeviceSummary>()));
    }
}
