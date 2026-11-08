# Tasks: 003-foreground-monitoring

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Контракт

- [x] T001 Порт IForegroundSessionHost + INotificationPermission + снимок/состояния/ошибки — `src/BabyMonitor.Application/Abstractions/Monitoring/`
- [x] T002 Fake + contract suite (start/stop/идемпотентность/события/ошибки/отмена) — `tests/BabyMonitor.Infrastructure.Tests/Monitoring/`
- [x] T003 Application unit-тесты инвариантов снимка — `tests/BabyMonitor.Application.Tests/Monitoring/`

## Phase 2 — Android

- [x] T004 MonitoringForegroundService: notification, Stop action, FGS-тип по роли — `src/BabyMonitor.Platform.Android/ForegroundServices/`
- [x] T005 AndroidForegroundSessionHost: binder, видимость Activity, timeout/cleanup — `src/BabyMonitor.Platform.Android/ForegroundServices/`
- [x] T006 POST_NOTIFICATIONS adapter + IActivityVisibility + MainActivity — `src/BabyMonitor.Platform.Android/Permissions/`, `src/BabyMonitor.App/`
- [x] T007 Manifest permissions + architecture tests (permissions, FGS-типы, без RECORD_AUDIO) — `src/BabyMonitor.App/AndroidManifest.xml`, `tests/BabyMonitor.Architecture.Tests/`

## Phase 3 — Проверки

- [x] T008 Local gates: build Debug/Release, 4 test-проекта, format — `BabyMonitor.slnx` (выполняется)
- [x] T009 Emulator smoke: APK install, POST_NOTIFICATIONS grant, service объявлен; adb-старт
  non-exported service отклонён Android ("Requires permission not exported") — runtime-проверка
  возможна только через UI (006)/instrumentation, отмечено как ограничение
- [-] T010 Android 12 FGS/notification PASS (2 Huawei, 2026-11-08); Activity destroyed/13+/14+ — 007
- [-] T011 Battery/Doze/OEM поведение — **BLOCKED** (перенесено в 007)
- [x] T012 PR #9 → CI → независимое ревью → merge — GitHub

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Build + tests | build 0 warnings; Domain 6, Application 81, Architecture 43, Infrastructure 60 = 190/190 pass; format exit 0 |
| 2026-11-07 | Emulator smoke (AVD API 35) | install OK; POST_NOTIFICATIONS granted; service объявлен в package; adb-старт non-exported service отклонён Android — runtime/notification BLOCKED без UI/instrumentation |
| 2026-11-07 | Review fixes | MAJOR: RECORD_AUDIO для Child FGS microphone (Android 14+) + permission plumbing; MINOR/NIT закрыты; 190/190 tests |
| 2026-11-07 | Device matrix | (исторически) BLOCKED: нет устройств |
| 2026-11-08 | Device pass v0.3.1 | FGS/notification/screen-off/Stop PASS на 2 Huawei/Android 12; Activity destroyed/13+/14+/Doze — 007 |