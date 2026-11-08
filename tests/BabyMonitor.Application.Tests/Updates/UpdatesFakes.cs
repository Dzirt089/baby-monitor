using BabyMonitor.Application.Abstractions.Connectivity;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;
using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Application.Tests.Updates;

/// <summary>Управляемое время для throttle/LastSuccessfulCheckAt.</summary>
internal sealed class FakeUpdateTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>Fake IAppUpdateSource: статический результат или управляемый handler.</summary>
internal sealed class FakeAppUpdateSource : IAppUpdateSource
{
    public int Calls { get; private set; }

    public Result<AppUpdateManifest> Result { get; set; } = Result<AppUpdateManifest>.Failure(
        new Error(AppUpdateErrorCodes.InvalidManifest, "Источник не настроен в тесте."));

    public Func<CancellationToken, Task<Result<AppUpdateManifest>>>? Handler { get; set; }

    public Task<Result<AppUpdateManifest>> GetLatestManifestAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Handler is { } handler ? handler(cancellationToken) : Task.FromResult(Result);
    }
}

/// <summary>Fake IUpdatePackageDownloader: результат, переданные аргументы и порядок вызовов.</summary>
internal sealed class FakeUpdatePackageDownloader : IUpdatePackageDownloader
{
    public int Calls { get; private set; }

    public AppUpdateManifest? LastManifest { get; private set; }

    public IProgress<double>? LastProgress { get; private set; }

    public Result<DownloadedUpdatePackage> Result { get; set; } = Result<DownloadedUpdatePackage>.Failure(
        new Error(AppUpdateErrorCodes.DownloadFailed, "Downloader не настроен в тесте."));

    public Func<AppUpdateManifest, IProgress<double>?, CancellationToken, Task<Result<DownloadedUpdatePackage>>>? Handler { get; set; }

    public Task<Result<DownloadedUpdatePackage>> DownloadAsync(
        AppUpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        Calls++;
        LastManifest = manifest;
        LastProgress = progress;
        return Handler is { } handler
            ? handler(manifest, progress, cancellationToken)
            : Task.FromResult(Result);
    }
}

/// <summary>Fake IUpdateInstaller: результат и переданный пакет.</summary>
internal sealed class FakeUpdateInstaller : IUpdateInstaller
{
    public int Calls { get; private set; }

    public DownloadedUpdatePackage? LastPackage { get; private set; }

    public Result Result { get; set; } = Result.Success();

    public Task<Result> InstallAsync(DownloadedUpdatePackage package, CancellationToken cancellationToken)
    {
        Calls++;
        LastPackage = package;
        return Task.FromResult(Result);
    }
}

/// <summary>Fake IAppVersionProvider: установленная версия/packageId.</summary>
internal sealed class FakeAppVersionProvider : IAppVersionProvider
{
    public FakeAppVersionProvider(AppVersion currentVersion, string packageId)
    {
        CurrentVersion = currentVersion;
        PackageId = packageId;
    }

    public AppVersion CurrentVersion { get; set; }

    public string PackageId { get; }

    public AppVersion GetCurrentVersion() => CurrentVersion;
}

/// <summary>Fake IUpdateSettingsStore: in-memory настройки автообновления.</summary>
internal sealed class FakeUpdateSettingsStore : IUpdateSettingsStore
{
    public bool AutoCheckEnabled { get; set; } = true;

    public DateTimeOffset? LastSuccessfulCheckAt { get; set; }
}

/// <summary>Fake IConnectivityMonitor: настраиваемый снимок сети и счётчик запросов.</summary>
internal sealed class FakeUpdateConnectivityMonitor : IConnectivityMonitor
{
    public int Calls { get; private set; }

    public Result<NetworkSnapshot> SnapshotResult { get; set; } =
        Result<NetworkSnapshot>.Success(new NetworkSnapshot(true));

    public void GoOffline() => SnapshotResult = Result<NetworkSnapshot>.Success(new NetworkSnapshot(false));

    public void GoOnline() => SnapshotResult = Result<NetworkSnapshot>.Success(new NetworkSnapshot(true));

    public event EventHandler? NetworkChanged;

    public Task<Result<NetworkSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(SnapshotResult);
    }

    public void RaiseNetworkChanged() => NetworkChanged?.Invoke(this, EventArgs.Empty);

    public ValueTask DisposeAsync()
    {
        NetworkChanged = null;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Fake IProgress для проверки проброса загрузки.</summary>
internal sealed class FakeDownloadProgress : IProgress<double>
{
    public List<double> Values { get; } = new();

    public void Report(double value) => Values.Add(value);
}
