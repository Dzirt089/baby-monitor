# Implementation Plan: 009 — App auto-update

## Архитектура

- **Domain**: AppVersion (versionCode + versionName) и компаратор (обновление только versionCode >;
  equal/downgrade reject). Без Android/HTTP.
- **Application**:
  - AppUpdateManifest (record): schemaVersion, packageId, versionName, versionCode, apkAssetName,
    sha256, sizeBytes, notes, publishedAt.
  - Порты: IAppUpdateSource (fetch manifest), IUpdatePackageDownloader (download+sha/size verify),
    IUpdateInstaller (signature verify + PackageInstaller), IAppVersionProvider,
    IUpdateSettingsStore (autoCheck enabled, lastCheckAt), IConnectivityMonitor (existing).
  - AppUpdateCoordinator: CheckAsync(trigger Manual/Auto), DownloadAndInstallAsync; Wi-Fi gate,
    24h throttle (TimeProvider), typed AppUpdateErrorCodes.
- **Infrastructure**: GitHubReleaseUpdateSource (HttpClient GET manifest, JSON schema, HTTPS-only,
  timeout), ApkUpdateDownloader (stream to cache, incremental SHA-256, size limit), tests with fake
  HttpMessageHandler/temp files.
- **Platform.Android**: AndroidAppVersionProvider, AndroidUpdateSettingsStore (SharedPreferences),
  AndroidApkUpdateInstaller: signature cert SHA-256 compare, PackageInstaller session, FileProvider,
  unknown-sources settings intent. Manifest permissions INTERNET/REQUEST_INSTALL_PACKAGES.
- **App**: UpdateActivity + ViewModel; auto-check once per launch/day from MainActivity (background,
  Wi-Fi only), dialog if update; button «Проверить обновления»; checkbox; composition wiring; strings.

## Поток

Check: Wi-Fi? + throttle (auto) → HTTP manifest → schema/packageId validate → compare versionCode →
Result<AppUpdateInfo>. Download: Wi-Fi → GET APK → temp file + SHA-256/size verify → installer.
Install: signature/package/versionCode checks → PackageInstaller session → user confirmation → after
install app restart (user).

## Безопасность

- Публичный repo → no token; HTTPS default trust; no cleartext.
- SHA-256+size из манифеста + проверка подписи APK против установленной — защита от подмены/повреждения.
- Отказ для downgrade/equal, wrong packageId/signature/hash, unsupported schema.
- Никаких секретов/PII в логах и манифесте.
- User-confirmed install only; REQUEST_INSTALL_PACKAGES минимум.

## Release workflow

- В release.yml после APK+checksum: сформировать update-manifest.json (packageId, versionName,
  versionCode, apkAssetName, sha256, sizeBytes, notes, publishedAt); приложить к Release
  (gh release upload). Проверить releases/latest/download/update-manifest.json.
- Первый релиз с фичей: v0.5.0; для device-проверки опубликовать v0.5.1 (update flow).

## Риски

- GitHub latest-release asset redirect/ratelimit — используем not API, а releases/latest/download;
  device-test обязателен.
- Huawei installer/unknown sources UX отличается — device-test.
- Auto-check при запуске не должен мешать мониторингу (background, non-blocking, Wi-Fi only).
- Signature retrieval APIs differ API 24–27 vs 28+; fallback signatures.
