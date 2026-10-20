# Tasks: 000-bootstrap

Единственный task ledger bootstrap-фазы. Статусы: [ ] не начато, [-] заблокировано (+причина),
[x] завершено и проверено. Задача не отмечается [x] без фактической проверки.

## Phase 1 — Локальный каркас

- [x] T001 Создать PROJECT_DIR baby-monitor и базовые .gitignore/.gitattributes/.editorconfig — `baby-monitor/`
- [x] T002 Создать BabyMonitor.slnx с 5 src- и 3 test-проектами — `BabyMonitor.slnx`
- [x] T003 Настроить ProjectReference по направлению зависимостей — `src/**/*.csproj`
- [x] T004 Добавить минимальный доменный контракт ролей — `src/BabyMonitor.Domain/Devices/`
- [x] T005 Добавить контракт Result/Error — `src/BabyMonitor.Application/Results/`
- [x] T006 Добавить маркеры Infrastructure и Platform.Android — `src/BabyMonitor.Infrastructure/`, `src/BabyMonitor.Platform.Android/`
- [x] T007 Добавить честный Android-каркас без разрешений — `src/BabyMonitor.App/`
- [x] T008 Локально собрать Debug и Release без warnings — `BabyMonitor.slnx`
- [x] T009 Собрать debug APK и проверить ApplicationId/versionName — `src/BabyMonitor.App/bin/Debug/net10.0-android/`

## Phase 2 — Тесты

- [x] T010 Подготовить test-проекты (xUnit, CPM, references) — `tests/**/*.csproj`
- [x] T011 Domain-тесты DeviceRole.Complement — `tests/BabyMonitor.Domain.Tests/` (6 тестов)
- [x] T012 Application-тесты Result/Error — `tests/BabyMonitor.Application.Tests/` (20 тестов)
- [x] T013 Architecture tests (dependency direction/TFM/CPM/AGENTS) — `tests/BabyMonitor.Architecture.Tests/` (32 теста)
- [x] T014 Прогнать dotnet test по всем test-проектам — `tests/` (58/58 pass)
- [x] T015 Smoke-проверка debug APK на Android-эмуляторе (serial emulator-5554, Android 15/API 35) — `artifacts/`

## Phase 3 — Документация

- [x] T016 Скопировать AGENTS.md без изменений в корень проекта — `AGENTS.md`
- [x] T017 README/CHANGELOG/LICENSE — `README.md`, `CHANGELOG.md`, `LICENSE`
- [x] T018 Конституция проекта — `.specify/memory/constitution.md`
- [x] T019 Bootstrap spec/plan/tasks/checklist — `.specify/specs/000-bootstrap/`
- [x] T020 Vision и roadmap — `docs/product/`
- [x] T021 Architecture overview — `docs/architecture/architecture.md`
- [x] T022 ADR template + ADR-0001..0009 — `docs/architecture/adr/`

## Phase 4 — CI/CD и GitHub-шаблоны

- [x] T023 ci.yml (restore/build/test/APK artifact, minimal permissions, pinned actions) — `.github/workflows/ci.yml`
- [x] T024 release.yml (SemVer tag, secrets gate, signing cleanup, checksum, release) — `.github/workflows/release.yml`
- [x] T025 dependabot.yml + release notes config — `.github/dependabot.yml`, `.github/release.yml`
- [x] T026 ISSUE_TEMPLATE + PULL_REQUEST_TEMPLATE — `.github/ISSUE_TEMPLATE/`, `.github/PULL_REQUEST_TEMPLATE.md`

## Phase 5 — Ревью и проверки

- [x] T027 Reviewer pass 1 (каркас) и pass 2 (docs/workflows/tests), исправить BLOCKER/MAJOR — `docs/architecture/` (0 blocker/major; MINOR/NIT исправлены, остаток принят)
- [x] T028 Повторить quality gates после исправлений — `BabyMonitor.slnx` (58/58, 0 warnings, APK, format, vulnerable)
- [x] T029 Scan: secrets/absolute paths/keystore/APK/bin/obj/artifacts не трекаются — `.`
- [x] T030 YAML/workflow sanity + README-команды соответствуют реальным путям — `.github/`, `README.md`

## Phase 6 — Git и GitHub

- [x] T031 Инициализировать git (branch main) — `.git/`
- [x] T031a Создать initial commit в main — `.git/` (29a5624)
- [x] T032 Создать feature branch bootstrap/initial-foundation и commit каркаса — `.git/` (ee8c7e1)
- [x] T033 Создать private GitHub repository baby-monitor без auto-init — GitHub (Dzirt089/baby-monitor, private)
- [x] T034 Push main и feature branch, открыть PR (не merge) — GitHub (PR #1)
- [x] T035 Прочитать статус CI run на PR и зафиксировать факт — GitHub Actions (run 39456208203: success)
- [x] T036 Зафиксировать manual checklist настроек GitHub (branch protection/env/secrets/labels) — отчёт (T036 checklist в финальном отчёте)

## Verification Log

| Дата | Проверка | Команда/способ | Результат |
|---|---|---|---|
| 2026-10-20 | Локальная сборка Debug/Release | dotnet restore + dotnet build -c Debug/Release | 0 warnings, 0 errors |
| 2026-10-20 | Debug APK | dotnet publish ... -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true | APK собран, package com.example.babymonitor, versionCode 100, versionName 0.1.0 |
| 2026-10-20 | YAML синтаксис | PyYAML safe_load по всем .github/**/*.yml | 7/7 OK |
| 2026-10-20 | AGENTS.md неизменность | SHA-256 сравнение с оригиналом | совпадает |
| 2026-10-20 | Secret/path scan | Select-String по трекаемым кандидатам | совпадений нет |
| 2026-10-20 | Тесты | dotnet test (Release, --no-build) | Domain 6/6, Application 20/20, Architecture 32/32 = 58 pass, exit 0 |
| 2026-10-20 | Debug APK self-contained | dotnet publish -p:EmbedAssembliesIntoApk=true | APK 43.5 MB, ABIs arm64-v8a+x86_64, versionCode 100, versionName 0.1.0 |
| 2026-10-20 | Smoke на эмуляторе | AVD Android_Emulator_35, serial emulator-5554, adb install/monkey/uiautomator | MainActivity запущена, crash buffer пуст, тексты экрана найдены |
| 2026-10-20 | Release manifest | разбор obj/Release/.../AndroidManifest.xml | нет permissions, debuggable=false |
| 2026-10-20 | Format | dotnet format --verify-no-changes | exit 0 |
| 2026-10-20 | Dependency scan | dotnet list package --vulnerable --include-transitive | уязвимых пакетов не найдено (8 проектов) |
| 2026-10-20 | Git hygiene | git init + git ls-files --others --exclude-standard + check-ignore | 84 кандидата, bin/obj/artifacts/APK игнорируются, неожиданных нет |
| 2026-10-20 | Workflow bash syntax | PyYAML + bash -n по всем run-блокам | 7 YAML OK, bash syntax OK |
| 2026-10-20 | GitHub repository | MCP: create private repo | Dzirt089/baby-monitor, private, default main, auto-init=false |
| 2026-10-20 | Push + PR | push main/bootstrap/initial-foundation | PR #1 открыт, не merged |
| 2026-10-20 | Remote CI | GitHub Actions run 39456208203 (pull_request) | success; job build/test/debug-APK success |
| 2026-10-20 | Labels | REST: 9 labels созданы | type:*, area:*, status:blocked, skip-changelog |
| 2026-10-20 | Независимое ревью pass 1 | reviewer (отдельный агент) | PASS WITH COMMENTS, 0 blocker/major; MINOR/NIT исправлены |
| 2026-10-20 | Независимое ревью pass 2 | reviewer (отдельный агент) | PASS WITH COMMENTS; checklist 10/10 PASS; MINOR/NIT исправлены до commit |

## Debug APK smoke (факты)

- Первая попытка на bin/Debug/...-Signed.apk (обычный build) выявила fast deployment: приложение
  падало с сообщением про Fast Deployment. Исправлено: CI и README используют
  dotnet publish с -p:EmbedAssembliesIntoApk=true; повторная проверка на emulator-5554 успешна.
- Debug-манифест содержит INTERNET-разрешение, которое .NET Android добавляет в debug-сборку
  автоматически (debugger/profiler); Release-манифест разрешений не содержит. Debug APK не
  предназначен для распространения.
