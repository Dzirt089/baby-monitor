using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Порт установки APK-обновления. Реализация — Platform.Android: проверка signing certificate
/// SHA-256 против установленного приложения, packageId/versionCode и PackageInstaller-сессия
/// с обязательным подтверждением пользователя (silent install запрещён).
/// </summary>
public interface IUpdateInstaller
{
    /// <summary>
    /// Проверяет подпись/версию и запускает установку через PackageInstaller.
    /// </summary>
    /// <param name="package">Скачанный и проверенный APK.</param>
    /// <param name="cancellationToken">Отмена запроса установки.</param>
    /// <returns>Успех либо typed ошибка (unknown sources, signature/hash, installer).</returns>
    Task<Result> InstallAsync(DownloadedUpdatePackage package, CancellationToken cancellationToken);
}
