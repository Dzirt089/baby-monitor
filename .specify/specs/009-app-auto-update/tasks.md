# Tasks: 009 — App auto-update

## Phase 1 — Contracts/Domain
- [x] T001 Domain AppVersion + version comparison tests.
- [x] T002 Application records/ports/error codes (AppUpdateManifest, source/downloader/installer/
  version/settings ports) + XML-doc.
- [x] T003 Application AppUpdateCoordinator (check/download/install, Wi-Fi gate, 24h throttle,
  enabled flag, TimeProvider) + unit tests (fakes).

## Phase 2 — Infrastructure
- [x] T004 GitHubReleaseUpdateSource (manifest URL constant, HttpClient, JSON schema v1, HTTPS,
  timeout, typed errors) + tests.
- [x] T005 ApkUpdateDownloader (stream to cache, SHA-256/size verify, cleanup on failure) + tests.

## Phase 3 — Platform.Android
- [x] T006 AndroidAppVersionProvider, AndroidUpdateSettingsStore (SharedPreferences), DI/composition.
- [x] T007 AndroidApkUpdateInstaller: cert SHA-256 compare, package/version checks, PackageInstaller,
  FileProvider, unknown-sources intent; manifest permission REQUEST_INSTALL_PACKAGES.
- [x] T008 Signature verification tests/adapters where possible; manual device checks.

## Phase 4 — UI
- [x] T009 UpdateActivity/ViewModel: current/latest, check button, status/error, download+install button,
  auto-check checkbox; strings; a11y.
- [x] T010 Auto-check on app launch (once/24h, Wi-Fi only, non-blocking) + update dialog; button on
  main/role screen; disable works.

## Phase 5 — Release/integration
- [ ] T011 (manifest generation готов, release pending) version 0.5.0; release.yml generates/uploads update-manifest.json; verify asset URL.
- [ ] T012 README/ADR-0012/roadmap/CHANGELOG/threat notes; tests/gates; independent review; CI.
- [ ] T013 Release v0.5.0 → install both phones → publish v0.5.1 → device test update flow
  (manual + auto, Wi-Fi-only, disable, signature/hash negative only unit) → document.

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-09 | 009 code gates | Domain 48, Application 310, Infrastructure 219, Architecture 59 = 636/636; Debug/Release 0 warnings; format clean; vuln OK |
| — | — | — |