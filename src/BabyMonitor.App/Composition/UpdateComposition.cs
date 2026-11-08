using Android.Content;
using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Updates;
using BabyMonitor.Infrastructure.Updates;
using BabyMonitor.Platform.Android.Connectivity;
using BabyMonitor.Platform.Android.Updates;
using UpdateCheckResult = BabyMonitor.Application.Results.Result<BabyMonitor.Application.Updates.AppUpdateInfo?>;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Composition root обновлений (singleton на процесс, владелец — BabyMonitorApplication):
/// HttpClient с таймаутом 20 с, GitHub Releases source, APK downloader во внутреннем кэше,
/// Android-адаптеры версии/настроек/установки, connectivity monitor для Wi-Fi gate и
/// AppUpdateCoordinator. Activity получает ViewModel отсюда, а не собирает зависимости сама.
/// </summary>
internal sealed class UpdateComposition : IAsyncDisposable
{
    private const int HttpTimeoutSeconds = 20;
    private const string CacheSubdirectory = "updates";
    private const string UserAgentPrefix = "BabyMonitor/";

    private readonly HttpClient _httpClient;
    private readonly AndroidConnectivityMonitor _connectivity;
    private readonly IUpdateSettingsStore _settings;
    private readonly IAppVersionProvider _versionProvider;
    private readonly UpdateInstallStatusSource _installStatusSource;
    private int _disposed;

    /// <summary>Создаёт composition root обновлений.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">Недоступен каталог кэша приложения.</exception>
    public UpdateComposition(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext ?? context;
        var timeProvider = TimeProvider.System;

        _versionProvider = new AndroidAppVersionProvider(applicationContext);
        _settings = new AndroidUpdateSettingsStore(applicationContext);
        _installStatusSource = new UpdateInstallStatusSource(applicationContext);
        _connectivity = new AndroidConnectivityMonitor(applicationContext);
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
            UserAgentPrefix + _versionProvider.GetCurrentVersion().VersionName);

        var source = new GitHubReleaseUpdateSource(_httpClient, timeProvider);
        var downloader = new ApkUpdateDownloader(_httpClient, ResolveCacheDirectory(applicationContext), timeProvider);
        var installer = new AndroidApkUpdateInstaller(applicationContext);
        Coordinator = new AppUpdateCoordinator(
            source,
            downloader,
            installer,
            _versionProvider,
            _settings,
            _connectivity,
            timeProvider);
    }

    /// <summary>Application use case проверки/скачивания/установки обновлений.</summary>
    public AppUpdateCoordinator Coordinator { get; }

    /// <summary>Включена ли автопроверка (persisted настройка).</summary>
    public bool AutoCheckEnabled => _settings.AutoCheckEnabled;

    /// <summary>Создаёт ViewModel экрана обновлений (per-Activity).</summary>
    public UpdateViewModel CreateViewModel() =>
        new(Coordinator, _settings, _versionProvider, _installStatusSource);

    /// <summary>Фоновая автопроверка при запуске (throttle/Wi-Fi gate — внутри координатора).</summary>
    /// <param name="cancellationToken">Отмена при завершении Activity.</param>
    public Task<UpdateCheckResult> CheckAutomaticAsync(CancellationToken cancellationToken) =>
        Coordinator.CheckAsync(AppUpdateCheckTrigger.Automatic, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _installStatusSource.Dispose();
        _httpClient.Dispose();
        await _connectivity.DisposeAsync().ConfigureAwait(false);
    }

    private static string ResolveCacheDirectory(Context context)
    {
        var cacheDirectory = context.CacheDir?.AbsolutePath;
        if (string.IsNullOrWhiteSpace(cacheDirectory))
        {
            throw new InvalidOperationException("Кэш приложения недоступен для загрузки обновлений.");
        }

        return Path.Combine(cacheDirectory, CacheSubdirectory);
    }
}
