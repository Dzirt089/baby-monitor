using BabyMonitor.Application.Abstractions.Connectivity;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Application use case проверки и установки обновлений приложения (009, ADR-0012):
/// Wi-Fi-only gate, суточный throttle автопроверки, сравнение versionCode, скачивание и установка
/// через порты. Не содержит HTTP/Android/JSON-деталей; секреты и PII не логируются.
/// </summary>
/// <remarks>
/// Операции сериализованы: повторный вызов во время активной проверки/установки возвращает
/// <see cref="AppUpdateErrorCodes.Busy"/>. Отмена пробрасывается как <see cref="OperationCanceledException"/>.
/// </remarks>
public sealed class AppUpdateCoordinator
{
    private const int SupportedSchemaVersion = 1;

    /// <summary>Интервал не чаще которого выполняется автоматическая проверка.</summary>
    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private readonly IAppUpdateSource _source;
    private readonly IUpdatePackageDownloader _downloader;
    private readonly IUpdateInstaller _installer;
    private readonly IAppVersionProvider _versionProvider;
    private readonly IUpdateSettingsStore _settings;
    private readonly IConnectivityMonitor _connectivity;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Создаёт координатор обновлений.</summary>
    /// <param name="source">Источник манифеста последнего релиза.</param>
    /// <param name="downloader">Загрузчик APK с проверкой SHA-256/размера.</param>
    /// <param name="installer">Установщик APK (проверка подписи/versionCode, PackageInstaller).</param>
    /// <param name="versionProvider">Провайдер установленной версии и packageId.</param>
    /// <param name="settings">Сохранённые настройки автообновления (enabled, lastCheck).</param>
    /// <param name="connectivity">Порт состояния сети для Wi-Fi gate.</param>
    /// <param name="timeProvider">Инжектированное время (fake в тестах, throttle 24h).</param>
    /// <exception cref="ArgumentNullException">Любой обязательный порт равен null.</exception>
    public AppUpdateCoordinator(
        IAppUpdateSource source,
        IUpdatePackageDownloader downloader,
        IUpdateInstaller installer,
        IAppVersionProvider versionProvider,
        IUpdateSettingsStore settings,
        IConnectivityMonitor connectivity,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(installer);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _source = source;
        _downloader = downloader;
        _installer = installer;
        _versionProvider = versionProvider;
        _settings = settings;
        _connectivity = connectivity;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Проверяет наличие обновления. Для <see cref="AppUpdateCheckTrigger.Automatic"/> учитывает
    /// выключенную автопроверку (disabled) и суточный throttle; ручная проверка их игнорирует.
    /// Wi-Fi gate проверяется после throttle; успешная проверка (включая «обновлений нет»)
    /// обновляет <see cref="IUpdateSettingsStore.LastSuccessfulCheckAt"/>.
    /// </summary>
    /// <param name="trigger">Причина проверки: ручная или автоматическая.</param>
    /// <param name="cancellationToken">Отмена проверки.</param>
    /// <returns>
    /// Успех с <see cref="AppUpdateInfo"/> при наличии более новой версии; успех с null, если
    /// обновление не требуется (up-to-date или throttle); typed ошибка при отказе.
    /// </returns>
    /// <exception cref="ArgumentException">Неизвестный триггер.</exception>
    /// <exception cref="OperationCanceledException">Проверка отменена.</exception>
    public async Task<Result<AppUpdateInfo?>> CheckAsync(
        AppUpdateCheckTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (trigger is not (AppUpdateCheckTrigger.Manual or AppUpdateCheckTrigger.Automatic))
        {
            throw new ArgumentException("Триггер проверки должен быть Manual или Automatic.", nameof(trigger));
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_gate.Wait(0))
        {
            return Result<AppUpdateInfo?>.Failure(BusyError());
        }

        try
        {
            if (trigger is AppUpdateCheckTrigger.Automatic)
            {
                if (!_settings.AutoCheckEnabled)
                {
                    return Result<AppUpdateInfo?>.Failure(new Error(
                        AppUpdateErrorCodes.Disabled,
                        "Автоматическая проверка обновлений отключена."));
                }

                var lastCheckAt = _settings.LastSuccessfulCheckAt;
                if (lastCheckAt is not null
                    && _timeProvider.GetUtcNow() - lastCheckAt.Value < AutoCheckInterval)
                {
                    // Throttle проверяется до Wi-Fi gate: авто-проверка просто пропускается.
                    return Result<AppUpdateInfo?>.SuccessNullable(null);
                }
            }

            var networkError = await CheckLanNetworkAsync(cancellationToken);
            if (networkError is not null)
            {
                return Result<AppUpdateInfo?>.Failure(networkError);
            }

            var manifestResult = await _source.GetLatestManifestAsync(cancellationToken);
            if (!manifestResult.IsSuccess)
            {
                return Result<AppUpdateInfo?>.Failure(manifestResult.Error!);
            }

            var manifest = manifestResult.Value;
            if (manifest.SchemaVersion != SupportedSchemaVersion)
            {
                return Result<AppUpdateInfo?>.Failure(new Error(
                    AppUpdateErrorCodes.UnsupportedSchema,
                    "Версия формата обновления не поддерживается."));
            }

            if (!string.Equals(manifest.PackageId, _versionProvider.PackageId, StringComparison.Ordinal))
            {
                return Result<AppUpdateInfo?>.Failure(new Error(
                    AppUpdateErrorCodes.PackageMismatch,
                    "Обновление относится к другому приложению."));
            }

            _settings.LastSuccessfulCheckAt = _timeProvider.GetUtcNow();
            var currentVersion = _versionProvider.GetCurrentVersion();
            var candidateVersion = new AppVersion(manifest.VersionCode, manifest.VersionName);
            if (!AppVersion.IsNewerThan(candidateVersion, currentVersion))
            {
                return Result<AppUpdateInfo?>.SuccessNullable(null);
            }

            return Result<AppUpdateInfo?>.Success(new AppUpdateInfo(currentVersion, manifest));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Скачивает и устанавливает обновление по манифесту: Wi-Fi gate, downloader (SHA-256/size),
    /// затем installer (signature/PackageInstaller). Ошибки портов пробрасываются как есть.
    /// </summary>
    /// <param name="manifest">Манифест обновления, полученный из <see cref="CheckAsync"/>.</param>
    /// <param name="progress">Прогресс загрузки 0..1 либо null.</param>
    /// <param name="cancellationToken">Отмена загрузки/установки.</param>
    /// <returns>Успех либо typed ошибка портов/сети.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manifest"/> равен null.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена.</exception>
    public async Task<Result> DownloadAndInstallAsync(
        AppUpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_gate.Wait(0))
        {
            return Result.Failure(BusyError());
        }

        try
        {
            var networkError = await CheckLanNetworkAsync(cancellationToken);
            if (networkError is not null)
            {
                return Result.Failure(networkError);
            }

            var downloaded = await _downloader.DownloadAsync(manifest, progress, cancellationToken);
            if (!downloaded.IsSuccess)
            {
                return Result.Failure(downloaded.Error!);
            }

            var installed = await _installer.InstallAsync(downloaded.Value, cancellationToken);
            if (!installed.IsSuccess)
            {
                return Result.Failure(installed.Error!);
            }

            return Result.Success();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Error?> CheckLanNetworkAsync(CancellationToken cancellationToken)
    {
        var snapshot = await _connectivity.GetSnapshotAsync(cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return new Error(
                AppUpdateErrorCodes.Network,
                "Не удалось проверить состояние сети.");
        }

        return snapshot.Value.HasLanNetwork
            ? null
            : new Error(
                AppUpdateErrorCodes.NoWifi,
                "Обновления доступны только по Wi-Fi.");
    }

    private static Error BusyError() => new(
        AppUpdateErrorCodes.Busy,
        "Другая операция обновления уже выполняется.");
}
