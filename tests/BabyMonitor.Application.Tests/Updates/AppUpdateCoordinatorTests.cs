using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;
using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Application.Tests.Updates;

/// <summary>
/// Use case проверки/установки обновлений: сравнение версий, auto disabled/throttle, Wi-Fi-only,
/// schema/packageId validation, lastCheck, download+install порядок, ошибки портов, busy, отмена.
/// </summary>
public sealed class AppUpdateCoordinatorTests
{
    private const string PackageId = "com.example.babymonitor";

    private sealed class Harness
    {
        public FakeUpdateTimeProvider Time { get; } = new();

        public FakeAppUpdateSource Source { get; } = new();

        public FakeUpdatePackageDownloader Downloader { get; } = new();

        public FakeUpdateInstaller Installer { get; } = new();

        public FakeAppVersionProvider VersionProvider { get; } = new(new AppVersion(100, "0.4.6"), PackageId);

        public FakeUpdateSettingsStore Settings { get; } = new();

        public FakeUpdateConnectivityMonitor Connectivity { get; } = new();

        public AppUpdateCoordinator Create() => new(
            Source,
            Downloader,
            Installer,
            VersionProvider,
            Settings,
            Connectivity,
            Time);

        public static AppUpdateManifest Manifest(
            int versionCode = 101,
            string versionName = "0.5.0",
            int schemaVersion = 1,
            string packageId = PackageId) => new(
            schemaVersion,
            packageId,
            versionName,
            versionCode,
            "babymonitor-" + versionName + ".apk",
            new string('a', 64),
            123_456,
            "Release notes",
            new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Новая_версия_возвращает_обновление_и_обновляет_lastCheck()
    {
        var harness = new Harness();
        var manifest = Harness.Manifest();
        harness.Source.Result = Result<AppUpdateManifest>.Success(manifest);
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var info = result.Value!;
        Assert.Equal(manifest, info.Manifest);
        Assert.Equal(new AppVersion(100, "0.4.6"), info.CurrentVersion);
        Assert.Equal(1, harness.Source.Calls);
        Assert.Equal(1, harness.Connectivity.Calls);
        Assert.Equal(harness.Time.GetUtcNow(), harness.Settings.LastSuccessfulCheckAt);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(99)]
    public async Task Равная_или_старая_версия_даёт_нет_обновления(int manifestVersionCode)
    {
        var harness = new Harness();
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest(versionCode: manifestVersionCode));
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(harness.Time.GetUtcNow(), harness.Settings.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task Автопроверка_отключена_даёт_disabled_без_сети_и_источника()
    {
        var harness = new Harness();
        harness.Settings.AutoCheckEnabled = false;
        harness.Connectivity.GoOffline();
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Automatic, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Disabled, result.Error!.Code);
        Assert.Equal(0, harness.Source.Calls);
        Assert.Equal(0, harness.Connectivity.Calls);
    }

    [Fact]
    public async Task Автопроверка_throttle_24h_пропускается_до_WiFi_gate()
    {
        var harness = new Harness();
        harness.Settings.LastSuccessfulCheckAt = harness.Time.GetUtcNow() - TimeSpan.FromHours(1);
        harness.Connectivity.GoOffline();
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Automatic, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
        Assert.Equal(0, harness.Source.Calls);
        Assert.Equal(0, harness.Connectivity.Calls);
    }

    [Fact]
    public async Task Автопроверка_после_24h_уже_проверяет_сеть()
    {
        var harness = new Harness();
        harness.Settings.LastSuccessfulCheckAt = harness.Time.GetUtcNow() - TimeSpan.FromHours(25);
        harness.Connectivity.GoOffline();
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Automatic, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.NoWifi, result.Error!.Code);
        Assert.Equal(1, harness.Connectivity.Calls);
        Assert.Equal(0, harness.Source.Calls);
    }

    [Fact]
    public async Task Ручная_проверка_игнорирует_disabled_и_throttle()
    {
        var harness = new Harness();
        harness.Settings.AutoCheckEnabled = false;
        harness.Settings.LastSuccessfulCheckAt = harness.Time.GetUtcNow() - TimeSpan.FromMinutes(1);
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest());
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value);
        Assert.Equal(1, harness.Source.Calls);
    }

    [Theory]
    [InlineData(AppUpdateCheckTrigger.Manual)]
    [InlineData(AppUpdateCheckTrigger.Automatic)]
    public async Task Нет_WiFi_даёт_no_wifi(AppUpdateCheckTrigger trigger)
    {
        var harness = new Harness();
        harness.Connectivity.GoOffline();
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(trigger, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.NoWifi, result.Error!.Code);
        Assert.Equal(0, harness.Source.Calls);
    }

    [Fact]
    public async Task Неверная_схема_даёт_unsupported_schema()
    {
        var harness = new Harness();
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest(schemaVersion: 2));
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.UnsupportedSchema, result.Error!.Code);
        Assert.Null(harness.Settings.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task Чужой_packageId_даёт_package_mismatch()
    {
        var harness = new Harness();
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest(packageId: "com.other.app"));
        var coordinator = harness.Create();

        var result = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.PackageMismatch, result.Error!.Code);
        Assert.Null(harness.Settings.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task Успешный_Check_обновляет_LastSuccessfulCheckAt()
    {
        var harness = new Harness();
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest());
        var coordinator = harness.Create();

        await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.Equal(harness.Time.GetUtcNow(), harness.Settings.LastSuccessfulCheckAt);
    }

    [Fact]
    public async Task DownloadAndInstall_успех_передаёт_манифест_прогресс_и_пакет()
    {
        var harness = new Harness();
        var manifest = Harness.Manifest();
        var package = new DownloadedUpdatePackage(manifest, "/tmp/babymonitor-0.5.0.apk");
        harness.Downloader.Result = Result<DownloadedUpdatePackage>.Success(package);
        harness.Installer.Result = Result.Success();
        var progress = new FakeDownloadProgress();
        var coordinator = harness.Create();

        var result = await coordinator.DownloadAndInstallAsync(manifest, progress, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, harness.Downloader.Calls);
        Assert.Equal(1, harness.Installer.Calls);
        Assert.Equal(manifest, harness.Downloader.LastManifest);
        Assert.Same(progress, harness.Downloader.LastProgress);
        Assert.Same(package, harness.Installer.LastPackage);
    }

    [Fact]
    public async Task DownloadAndInstall_пробрасывает_ошибку_загрузки()
    {
        var harness = new Harness();
        var manifest = Harness.Manifest();
        var error = new Error(AppUpdateErrorCodes.DownloadFailed, "Скачивание не удалось.");
        harness.Downloader.Result = Result<DownloadedUpdatePackage>.Failure(error);
        var coordinator = harness.Create();

        var result = await coordinator.DownloadAndInstallAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Equal(0, harness.Installer.Calls);
    }

    [Fact]
    public async Task DownloadAndInstall_пробрасывает_ошибку_установки()
    {
        var harness = new Harness();
        var manifest = Harness.Manifest();
        var package = new DownloadedUpdatePackage(manifest, "/tmp/update.apk");
        var error = new Error(AppUpdateErrorCodes.InstallFailed, "Установка не удалась.");
        harness.Downloader.Result = Result<DownloadedUpdatePackage>.Success(package);
        harness.Installer.Result = Result.Failure(error);
        var coordinator = harness.Create();

        var result = await coordinator.DownloadAndInstallAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Equal(1, harness.Installer.Calls);
    }

    [Fact]
    public async Task DownloadAndInstall_нет_WiFi_даёт_no_wifi_без_загрузки()
    {
        var harness = new Harness();
        harness.Connectivity.GoOffline();
        var coordinator = harness.Create();

        var result = await coordinator.DownloadAndInstallAsync(
            Harness.Manifest(),
            null,
            CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.NoWifi, result.Error!.Code);
        Assert.Equal(0, harness.Downloader.Calls);
        Assert.Equal(0, harness.Installer.Calls);
    }

    [Fact]
    public async Task Повторный_check_во_время_активной_операции_даёт_busy()
    {
        var harness = new Harness();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Source.Handler = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            return Result<AppUpdateManifest>.Success(Harness.Manifest());
        };
        var coordinator = harness.Create();

        var first = coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var busy = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);

        Assert.False(busy.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Busy, busy.Error!.Code);

        release.TrySetResult();
        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(firstResult.IsSuccess);

        // После release семафор освобождён: следующая операция снова работает.
        var next = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);
        Assert.True(next.IsSuccess);
    }

    [Fact]
    public async Task Отмена_проверки_пробрасывает_OCE_и_освобождает_busy()
    {
        var harness = new Harness();
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Source.Handler = async cancellationToken =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result<AppUpdateManifest>.Success(Harness.Manifest());
        };
        var coordinator = harness.Create();

        var task = coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(TimeSpan.FromSeconds(10)));

        harness.Source.Handler = null;
        harness.Source.Result = Result<AppUpdateManifest>.Success(Harness.Manifest());
        var next = await coordinator.CheckAsync(AppUpdateCheckTrigger.Manual, CancellationToken.None);
        Assert.True(next.IsSuccess);
    }

    [Fact]
    public async Task Отмена_загрузки_пробрасывает_OCE_и_не_вызывает_installer()
    {
        var harness = new Harness();
        var manifest = Harness.Manifest();
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Downloader.Handler = async (_, _, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Result<DownloadedUpdatePackage>.Success(new DownloadedUpdatePackage(manifest, "/tmp/update.apk"));
        };
        var coordinator = harness.Create();

        var task = coordinator.DownloadAndInstallAsync(manifest, null, cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => task.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Equal(0, harness.Installer.Calls);
    }
}
