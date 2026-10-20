# Feature Specification: 000-bootstrap — основа репозитория Baby Monitor

**Feature Branch**: bootstrap/initial-foundation
**Created**: 2026-10-20
**Status**: In Progress (bootstrap-фаза)
**Input**: BOOTSTRAP_PROMPT.md и AGENTS.md проекта «Радионяня»

## Контекст

Нужен воспроизводимый фундамент репозитория, на который можно нанизывать feature-спецификации
MVP: solution с правильным направлением зависимостей, минимальный честный каркас приложения,
тесты, CI, release-процесс по SemVer-тегам, документация и журнал архитектурных решений.
Bootstrap **не реализует** мониторинг, аудио, обнаружение, сопряжение и foreground service.

## User Scenarios

### US-1: Разработчик поднимает проект с нуля (P1)

Разработчик клонирует репозиторий, устанавливает .NET SDK по global.json и workload android,
выполняет restore/build/test и получает debug APK без дополнительных секретов и ручных настроек.

**Acceptance**: команды из README выполняются на чистой машине с указанными prerequisities;
build завершается без warnings (warnings as errors); unit и architecture tests зелёные.

### US-2: CI проверяет каждый PR (P1)

CI на pull_request/push main/workflow_dispatch выполняет restore, build, tests и собирает
debug APK как artifact, не публикуя его как релиз и не имея signing secrets.

**Acceptance**: workflow содержит минимальные permissions, pinned actions, concurrency;
artifact debug-apk и результаты тестов доступны из run; никакие secrets не требуются.

### US-3: Владелец выпускает версию по тегу (P2)

Человек создаёт SemVer-тег vX.Y.Z. Release workflow повторно тестирует tagged commit, собирает
подписанный APK только при наличии secrets в защищённом Environment release, считает SHA-256 и
создаёт GitHub Release с APK и checksum. Без secrets workflow завершается ошибкой до публикации
(либо unsigned dry-run только вручную).

**Acceptance**: workflow не создаёт/не двигает теги, не печатает секреты, удаляет keystore в
always()-cleanup; versionCode монотонно вычисляется по формуле из ADR и совпадает с тегом.

### US-4: Агент понимает границы и правила (P1)

Новый AI-агент или разработчик открывает AGENTS.md, конституцию, architecture.md, ADR и
bootstrap-спеку и понимает, что можно делать в bootstrap-фазе и что запрещено.

**Acceptance**: документы существуют, не противоречат друг другу, ссылки рабочие; AGENTS.md
в корне проекта не изменён относительно предоставленного пользователем оригинала.

## Functional Requirements

- **FR-001**: Solution BabyMonitor (slnx) содержит ровно 5 src-проектов (Domain, Application,
  Infrastructure, Platform.Android, App) и 3 test-проекта (Domain.Tests, Application.Tests,
  Architecture.Tests). Пустые «на будущее» каталоги отсутствуют. Infrastructure.Tests и
  DeviceTests не создаются на bootstrap-фазе: bootstrap-промпт запрещает проекты без
  содержательных тестов; они появятся вместе с feature 001/005.
- **FR-002**: ProjectReference соответствуют строгому направлению зависимостей; Architecture.Tests
  проверяет это, а также TFM, ApplicationId, SupportedOSPlatformVersion=24, CPM и отсутствие
  запрещённых зависимостей в Domain/Application.
- **FR-003**: Domain содержит минимальный реальный контракт ролей (DeviceRole, Complement),
  Application — минимальный контракт Result/Error. Нет фиктивной бизнес-логики, заглушек и TODO.
- **FR-004**: App — компилируемый Android-проект (net10.0-android, min API 24), который собирает
  debug APK; экран честно сообщает, что мониторинг не реализован; manifest без разрешений.
- **FR-005**: Корневые конфиги (global.json, Directory.Build.props, Directory.Packages.props,
  .editorconfig, .gitignore, .gitattributes) обеспечивают воспроизводимую сборку и не пропускают
  артефакты/секреты в Git. В репозитории нет bin/obj/artifacts/APK/keystore/secrets.
- **FR-006**: CI workflow (pull_request, push main, workflow_dispatch) выполняет restore, build,
  tests, architecture tests, собирает debug APK artifact с retention 14 дней; минимальные
  permissions; pinned actions; concurrency с отменой устаревших run.
- **FR-007**: Release workflow (push tag vX.Y.Z, workflow_dispatch dry-run) валидирует SemVer и
  CHANGELOG, повторно тестирует, подписывает только при наличии secrets в Environment release,
  считает SHA-256, создаёт GitHub Release; не создаёт теги; keystore cleanup всегда.
- **FR-008**: Документация: README (статус Bootstrap/not functional yet, ограничения, команды,
  CI/release, запрет хранения secrets), vision, roadmap, architecture, constitution,
  ADR-template и ADR-0001..0009; bootstrap spec/plan/tasks/checklist.
- **FR-009**: Git-гигиена: initial commit, feature branch bootstrap/initial-foundation, private
  GitHub repository создания после локальной проверки, push и открытый bootstrap PR; PR не merge.
- **FR-010**: AGENTS.md в корне проекта побайтно совпадает с предоставленным пользователем.

## Non-Functional Requirements

- **NFR-001**: Build --no-restore проходит без warnings при TreatWarningsAsErrors=true.
- **NFR-002**: Architecture tests работают на Windows и Linux (CI ubuntu-latest), без внешних
  пакетов кроме xUnit/Test SDK.
- **NFR-003**: Ни один tracked file не содержит секретов, signing keys, absolute machine paths,
  keystore, APK/AAB, bin/obj/artifacts.
- **NFR-004**: CI и release workflows имеют минимальные permissions и не используют
  pull_request_target/непроверенные marketplace actions.
- **NFR-005**: README-команды соответствуют реальным именам solution/проектов; статус приложения
  не завышен (никаких заявлений о работающем мониторинге).

## Acceptance Criteria

- [x] AC-001: dotnet restore + build --no-restore + test (3 test-проекта) локально зелёные (58/58, 0 warnings).
- [x] AC-002: Debug APK собирается локально; ApplicationId com.example.babymonitor; versionCode 100, versionName 0.1.0; smoke на emulator-5554.
- [x] AC-003: Architecture tests подтверждают dependency direction и гигиену (32 теста, включая negative-кейсы).
- [x] AC-004: git status показывает только ожидаемые source/config/docs; bin/obj/artifacts игнорируются (проверено git check-ignore).
- [x] AC-005: Независимый reviewer подтвердил соответствие AGENTS.md и bootstrap-scope (pass 1+2: PASS WITH COMMENTS, 0 blocker/major, checklist 10/10).
- [ ] AC-006: Private GitHub repo создан, feature branch и PR открыты, PR не merged.
- [ ] AC-007: CI run на PR выполнен (или честно зафиксировано, что remote-проверка недоступна).
- [ ] AC-008: Список ручных GitHub settings (branch protection, environment release, secrets,
  labels/milestone) приложен в отчёте; secrets не создавались.

## Out of Scope

WebRTC/SIPSorcery/Opus, AudioRecord/AudioTrack, NSD/mDNS, pairing, foreground service, уведомления,
ViewModels/экраны ролей, любые runtime-пакеты, signing key/keystore, первый release, GitHub
milestone/issues (кроме labels при доступности), настройка branch protection через API.

## Dependencies & Assumptions

- .NET SDK 10.0.400, workload android, JDK 17+ (CI использует Temurin 21; локально проверено на 21),
  Android SDK platform android-36/build-tools 36.
- GitHub-доступ через MCP/токен пользователя; repository создаётся private без auto-init.
- MAUI-нагрузка не установлена; UI-фреймворк остаётся Proposed ADR, каркас — .NET for Android.
- APPLICATION_ID com.example.babymonitor и repo name baby-monitor — значения по умолчанию
  bootstrap-промпта; могут быть изменены владельцем до первого релиза.

## Success Criteria

- Новый участник воспроизводит сборку/тесты/APK строго по README.
- CI даёт стабильный зелёный результат на PR и не имеет доступа к signing secrets.
- Release невозможен без постоянного keystore и защищённого Environment release.
- Следующая feature (001-audio-transport-spike) может начаться без переделки каркаса.
