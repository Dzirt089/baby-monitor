namespace BabyMonitor.Application.Updates;

/// <summary>
/// Скачанный и уже проверенный (размер + SHA-256) APK-пакет обновления во внутреннем кэше.
/// Файл не является секретом; установка выполняется только через <see cref="IUpdateInstaller"/>.
/// </summary>
/// <param name="Manifest">Манифест, по которому скачан пакет.</param>
/// <param name="FilePath">Локальный путь к проверенному APK.</param>
public sealed record DownloadedUpdatePackage(AppUpdateManifest Manifest, string FilePath);
