namespace BabyMonitor.Application.Updates;

/// <summary>
/// Стабильные коды ошибок проверки/установки обновлений приложения. Без секретов и PII;
/// пользовательское сообщение отделяется от технической причины.
/// </summary>
public static class AppUpdateErrorCodes
{
    /// <summary>Нет Wi-Fi/LAN: обновления разрешены только по локальной сети (Wi-Fi gate).</summary>
    public const string NoWifi = "updates.no_wifi";

    /// <summary>Сетевая ошибка проверки манифеста/загрузки (без деталей и URL с параметрами).</summary>
    public const string Network = "updates.network";

    /// <summary>Манифест обновления некорректен или не может быть прочитан.</summary>
    public const string InvalidManifest = "updates.invalid_manifest";

    /// <summary>Неизвестная schemaVersion манифеста (поддерживается только 1).</summary>
    public const string UnsupportedSchema = "updates.unsupported_schema";

    /// <summary>PackageId манифеста не совпадает с установленным приложением.</summary>
    public const string PackageMismatch = "updates.package_mismatch";

    /// <summary>Версия обновления не новее установленной (downgrade/equal запрещены).</summary>
    public const string DowngradeOrEqual = "updates.downgrade_or_equal";

    /// <summary>SHA-256 скачанного APK не совпал с манифестом.</summary>
    public const string HashMismatch = "updates.hash_mismatch";

    /// <summary>Подпись APK не совпадает с подписью установленного приложения.</summary>
    public const string SignatureMismatch = "updates.signature_mismatch";

    /// <summary>Требуется разрешение установки из неизвестных источников (пользовательский flow).</summary>
    public const string UnknownSourcesRequired = "updates.unknown_sources_required";

    /// <summary>PackageInstaller недоступен на устройстве.</summary>
    public const string InstallerUnavailable = "updates.installer_unavailable";

    /// <summary>Другая операция обновления уже выполняется (busy).</summary>
    public const string Busy = "updates.busy";

    /// <summary>Установка APK завершилась ошибкой.</summary>
    public const string InstallFailed = "updates.install_failed";

    /// <summary>Скачивание APK завершилось ошибкой.</summary>
    public const string DownloadFailed = "updates.download_failed";

    /// <summary>Автоматическая проверка обновлений отключена пользователем.</summary>
    public const string Disabled = "updates.disabled";
}
