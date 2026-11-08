namespace BabyMonitor.Application.Updates;

/// <summary>
/// Манифест последнего релиза приложения (update-manifest.json). Immutable-контракт Application:
/// не содержит секретов/PII; SHA-256 и размер относятся к APK-ассету.
/// </summary>
/// <param name="SchemaVersion">Версия схемы манифеста; поддерживается только 1.</param>
/// <param name="PackageId">Android packageId приложения (должен совпасть с установленным).</param>
/// <param name="VersionName">Человекочитаемое имя версии для UI.</param>
/// <param name="VersionCode">Целочисленный код версии; обновление только при большем значении.</param>
/// <param name="ApkAssetName">Имя APK-ассета в GitHub Release (без пути).</param>
/// <param name="Sha256">Hex SHA-256 APK-ассета (lowercase) для проверки целостности.</param>
/// <param name="SizeBytes">Ожидаемый размер APK в байтах.</param>
/// <param name="Notes">Краткие release notes без PII; null, если не заданы.</param>
/// <param name="PublishedAt">Момент публикации релиза; null, если неизвестен.</param>
public sealed record AppUpdateManifest(
    int SchemaVersion,
    string PackageId,
    string VersionName,
    int VersionCode,
    string ApkAssetName,
    string Sha256,
    long SizeBytes,
    string? Notes,
    DateTimeOffset? PublishedAt);
