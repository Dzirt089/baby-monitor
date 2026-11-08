using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Порт загрузки APK-пакета обновления. Реализация — Infrastructure: HTTPS-загрузка в кэш,
/// инкрементальный SHA-256 и проверка размера; при несовпадении файл удаляется.
/// </summary>
public interface IUpdatePackageDownloader
{
    /// <summary>
    /// Скачивает APK по манифесту и проверяет размер/SHA-256.
    /// </summary>
    /// <param name="manifest">Манифест обновления (источник ожидаемых hash/size).</param>
    /// <param name="progress">Прогресс 0..1 либо null, если UI не нужен.</param>
    /// <param name="cancellationToken">Отмена загрузки.</param>
    /// <returns>Проверенный пакет либо typed ошибка.</returns>
    Task<Result<DownloadedUpdatePackage>> DownloadAsync(
        AppUpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken);
}
