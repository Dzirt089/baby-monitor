using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>
/// Модель экрана доверенных устройств: фильтр по локальной роли, сортировка, Forget,
/// read-only выдача и безопасные ошибки хранилища.
/// </summary>
public sealed class TrustedDevicesScreenModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly DateTimeOffset PairedAt = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

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

    private sealed class SwitchableTrustedDeviceStore : ITrustedDeviceStore
    {
        private readonly InMemoryTrustedDeviceStore _inner = new();

        public bool FailGetAll { get; set; }

        public bool FailRemove { get; set; }

        public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            _inner.FindAsync(fingerprintSha256, cancellationToken);

        public Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken) =>
            FailGetAll
                ? Task.FromResult(Result<IReadOnlyList<PairedDevice>>.Failure(StoreError()))
                : _inner.GetAllAsync(cancellationToken);

        public Task<Result> SaveAsync(PairedDevice device, CancellationToken cancellationToken) =>
            _inner.SaveAsync(device, cancellationToken);

        public Task<Result> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            FailRemove
                ? Task.FromResult(Result.Failure(StoreError()))
                : _inner.RemoveAsync(fingerprintSha256, cancellationToken);

        public Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
            _inner.IsTrustedAsync(fingerprintSha256, cancellationToken);

        private static Error StoreError() => new(PairingErrorCodes.StoreFailed, "Trusted store недоступен.");
    }

    [Fact]
    public async Task Refresh_фильтрует_устройства_по_локальной_роли()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        await store.SaveAsync(seed.Create(DeviceRole.Parent, PairedAt.AddMinutes(1)), None);
        var model = new TrustedDevicesScreenModel(store);

        await model.RefreshAsync(DeviceRole.Child, None);

        Assert.Single(model.Devices);
        Assert.Equal(DeviceRole.Child, model.Devices[0].LocalRole);
        Assert.Equal(DeviceRole.Parent, model.Devices[0].PeerRole);
        Assert.True(model.HasTrustedPeer);
        Assert.Null(model.ErrorCode);
        Assert.False(model.IsBusy);

        await model.RefreshAsync(DeviceRole.Parent, None);

        Assert.Single(model.Devices);
        Assert.Equal(DeviceRole.Parent, model.Devices[0].LocalRole);
        Assert.Equal(DeviceRole.Child, model.Devices[0].PeerRole);
    }

    [Fact]
    public async Task Refresh_сортирует_устройства_по_времени_сопряжения_по_убыванию()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        var older = seed.Create(DeviceRole.Child, PairedAt);
        var newer = seed.Create(DeviceRole.Child, PairedAt.AddMinutes(5));
        await store.SaveAsync(older, None);
        await store.SaveAsync(newer, None);
        var model = new TrustedDevicesScreenModel(store);

        await model.RefreshAsync(DeviceRole.Child, None);

        Assert.Equal(2, model.Devices.Count);
        Assert.Equal(newer.PairedAt, model.Devices[0].PairedAt);
        Assert.Equal(older.PairedAt, model.Devices[1].PairedAt);
    }

    [Fact]
    public async Task Refresh_пустого_store_даёт_пустой_список_без_ошибки()
    {
        var model = new TrustedDevicesScreenModel(new InMemoryTrustedDeviceStore());

        await model.RefreshAsync(DeviceRole.Child, None);

        Assert.Empty(model.Devices);
        Assert.False(model.HasTrustedPeer);
        Assert.Null(model.ErrorCode);
    }

    [Fact]
    public async Task Forget_удаляет_устройство_из_store_и_списка()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        var removed = seed.Create(DeviceRole.Child, PairedAt.AddMinutes(5));
        var remaining = seed.Create(DeviceRole.Child, PairedAt);
        await store.SaveAsync(removed, None);
        await store.SaveAsync(remaining, None);
        var model = new TrustedDevicesScreenModel(store);
        await model.RefreshAsync(DeviceRole.Child, None);

        await model.ForgetAsync(removed.PeerIdentity.FingerprintSha256, DeviceRole.Child, None);

        Assert.Single(model.Devices);
        Assert.Equal(remaining.PeerIdentity.FingerprintSha256, model.Devices[0].FingerprintSha256);
        Assert.False((await store.IsTrustedAsync(removed.PeerIdentity.FingerprintSha256, None)).Value);
        Assert.Null(model.ErrorCode);
    }

    [Fact]
    public async Task Refresh_ошибка_store_не_затирает_предыдущий_список()
    {
        using var seed = new DeviceSeed();
        var store = new SwitchableTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        var model = new TrustedDevicesScreenModel(store);
        await model.RefreshAsync(DeviceRole.Child, None);
        Assert.Single(model.Devices);
        store.FailGetAll = true;

        await model.RefreshAsync(DeviceRole.Child, None);

        Assert.Equal(PairingErrorCodes.StoreFailed, model.ErrorCode);
        Assert.NotNull(model.ErrorMessage);
        Assert.Single(model.Devices);
    }

    [Fact]
    public async Task Forget_ошибка_store_выставляет_store_failed_и_не_меняет_список()
    {
        using var seed = new DeviceSeed();
        var store = new SwitchableTrustedDeviceStore();
        var device = seed.Create(DeviceRole.Child, PairedAt);
        await store.SaveAsync(device, None);
        var model = new TrustedDevicesScreenModel(store);
        await model.RefreshAsync(DeviceRole.Child, None);

        store.FailRemove = true;
        await model.ForgetAsync(device.PeerIdentity.FingerprintSha256, DeviceRole.Child, None);

        Assert.Equal(PairingErrorCodes.StoreFailed, model.ErrorCode);
        Assert.Single(model.Devices);
    }

    [Fact]
    public async Task FingerprintShort_равен_восьми_hex_символам_и_полный_fingerprint_доступен()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        var device = seed.Create(DeviceRole.Child, PairedAt);
        await store.SaveAsync(device, None);
        var model = new TrustedDevicesScreenModel(store);

        await model.RefreshAsync(DeviceRole.Child, None);

        var summary = model.Devices[0];
        Assert.Equal(8, summary.FingerprintShort.Length);
        Assert.Matches("^[0-9a-f]{8}$", summary.FingerprintShort);
        Assert.Equal(64, summary.FingerprintSha256.Length);
        Assert.Equal(summary.FingerprintSha256[..8], summary.FingerprintShort);
        Assert.Equal(device.PeerIdentity.FingerprintSha256, summary.FingerprintSha256);
        Assert.DoesNotContain(summary.FingerprintSha256, summary.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Devices_отдаются_только_для_чтения()
    {
        using var seed = new DeviceSeed();
        var store = new InMemoryTrustedDeviceStore();
        await store.SaveAsync(seed.Create(DeviceRole.Child, PairedAt), None);
        var model = new TrustedDevicesScreenModel(store);
        await model.RefreshAsync(DeviceRole.Child, None);

        Assert.Null(model.Devices as PairedDeviceSummary[]);
    }

    [Fact]
    public async Task Некорректная_роль_бросает_ArgumentException()
    {
        var model = new TrustedDevicesScreenModel(new InMemoryTrustedDeviceStore());

        await Assert.ThrowsAsync<ArgumentException>(() => model.RefreshAsync(DeviceRole.Unknown, None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => model.ForgetAsync("fingerprint", DeviceRole.Unknown, None));
        await Assert.ThrowsAsync<ArgumentException>(
            () => model.ForgetAsync("   ", DeviceRole.Child, None));
    }
}
