# Радионяня (Baby Monitor)

Android-приложение, которое соединяет два телефона в одной локальной Wi-Fi сети и передаёт звук
от детского устройства к родительскому без обязательного облачного backend.

> **Статус: not functional yet.**
> Bootstrap-фаза и 001-audio-transport-spike (phase 1: контракт + contract tests) merged в `main`;
> документация, CI и release-процесс готовы. Мониторинг, обнаружение устройств, сопряжение,
> передача аудио и foreground service ещё не реализованы — приложение пока не выполняет функции
> радионяни. Device-проверки BLOCKED: нет реальных устройств (emulator не заменяет device-тесты).

## Зачем

- **Child / Ребёнок**: работает микрофон, звук передаётся только в сторону родителя;
  локальное воспроизведение отсутствует.
- **Parent / Родитель**: работает динамик или выбранное устройство вывода; микрофон
  не запрашивается и не открывается.
- Основной сценарий работает в одной Wi-Fi LAN без интернета.
- Достоверная индикация состояния и восстановление связи важнее минимальной задержки.

Приложение **не является медицинским изделием**, не сертифицировано как средство
безопасности и не заменяет присмотр взрослого.

## Границы MVP

Входит: один APK для обеих ролей, явный выбор роли, обнаружение в текущей Wi-Fi сети,
безопасное сопряжение, односторонний audio-only Child → Parent, foreground service,
постоянное уведомление с действием «Остановить», автоматическое переподключение,
локальная тревога на Parent при потере связи, диагностика без записи аудио.

Не входит: видео, интернет/облачный relay, запись и хранение аудио, двусторонняя связь,
ML/детектор плача, несколько одновременных сессий, аккаунты, реклама, аналитические SDK.
Подробнее — [docs/product/vision.md](docs/product/vision.md).

## Документация

- [AGENTS.md](AGENTS.md) — обязательные правила разработки и архитектурные границы.
- [.specify/memory/constitution.md](.specify/memory/constitution.md) — проверяемые инженерные принципы.
- [docs/product/vision.md](docs/product/vision.md) — цели, пользователи, non-goals.
- [docs/product/roadmap.md](docs/product/roadmap.md) — фазы и статус milestone.
- [docs/architecture/architecture.md](docs/architecture/architecture.md) — слои, зависимости, trust boundaries.
- [docs/architecture/adr/](docs/architecture/adr/) — журнал архитектурных решений.
- [.specify/specs/000-bootstrap/](.specify/specs/000-bootstrap/) — spec/plan/tasks текущей фазы.
- [CHANGELOG.md](CHANGELOG.md) — выпущенные пользовательские изменения.

## Требования к окружению

- .NET SDK — версия из [global.json](global.json) (10.0.400, feature band 10.0.4xx).
- Workload: `dotnet workload install android` (MAUI и другие workload не требуются).
- JDK 17 или новее; локальная проверка выполнена на JDK 21 (OpenJDK, поставляемый с Android SDK).
  CI использует Temurin 21 (см. `.github/workflows/ci.yml`).
- Android SDK: platform `android-36`, build-tools `36.0.0`, platform-tools. Обычно .NET Android
  находит SDK автоматически; если нет — задайте `ANDROID_HOME` (или `ANDROID_SDK_ROOT`).
- Для локальной проверки APK — Android-устройство или emulator.

Пример для Windows PowerShell (подставьте пути своего окружения; в репозиторий машинные пути
коммитить нельзя):

```powershell
$env:ANDROID_HOME = "<путь к Android SDK>"
$env:ANDROID_SDK_ROOT = $env:ANDROID_HOME
# JAVA_HOME обычно выставляется установщиком JDK; при необходимости:
# $env:JAVA_HOME = "<путь к JDK 17+>"
```

## Сборка и тесты

```powershell
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

Точечный запуск тестов:

```powershell
dotnet test tests/BabyMonitor.Domain.Tests/BabyMonitor.Domain.Tests.csproj
dotnet test tests/BabyMonitor.Application.Tests/BabyMonitor.Application.Tests.csproj
dotnet test tests/BabyMonitor.Architecture.Tests/BabyMonitor.Architecture.Tests.csproj
dotnet test tests/BabyMonitor.Infrastructure.Tests/BabyMonitor.Infrastructure.Tests.csproj
```

Debug APK (только для локального smoke-теста, не для распространения; собирается self-contained,
чтобы устанавливаться и запускаться на устройстве, а не зависеть от fast deployment):

```powershell
dotnet publish src/BabyMonitor.App/BabyMonitor.App.csproj -c Debug -f net10.0-android -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true -o artifacts/debug-apk
# APK: artifacts/debug-apk/io.github.dzirt089.babymonitor-Signed.apk
```

## CI

[.github/workflows/ci.yml](.github/workflows/ci.yml) запускается на `pull_request`,
`push` в `main` и вручную (`workflow_dispatch`). CI выполняет restore, build, тесты,
architecture tests и собирает debug APK, который публикуется только как artifact
(хранение 14 дней), но не как GitHub Release.

Скачать APK: открыть нужный run в GitHub Actions → блок `Artifacts` → `debug-apk`
(файл `io.github.dzirt089.babymonitor-Signed.apk`; это self-contained debug-сборка для локального smoke).

## Release

Релиз собирается [.github/workflows/release.yml](.github/workflows/release.yml) только по
SemVer-тегу `vMAJOR.MINOR.PATCH`, который создаёт человек. Workflow:

1. проверяет формат тега и наличие раздела версии в `CHANGELOG.md`;
2. заново выполняет restore/build/test;
3. собирает release APK и подписывает его **только** при наличии signing secrets
   в GitHub Environment `release` (manual approval — только на планах GitHub с protection rules);
4. считает SHA-256, создаёт GitHub Release и прикладывает APK и checksum;
5. никогда не создаёт и не двигает теги.

Если secrets не настроены, release job завершается понятной ошибкой до публикации;
диагностический dry-run (без production-подписи) доступен только ручным запуском workflow,
его артефакт не предназначен для установки как релиз.

> **ApplicationId:** `io.github.dzirt089.babymonitor` — утверждён в [ADR-0010](docs/architecture/adr/0010-final-application-id.md)
> до первого тега. После публикации смена id означает другое приложение и ломает цепочку обновлений.
> Если id когда-либо изменится — нужен новый ADR, обновление `TargetFrameworkTests` и README;
> release workflow читает ApplicationId из csproj через `dotnet msbuild -getProperty:ApplicationId`.

Первый release **не выполнялся**:
постоянный keystore и Environment `release` с signing secrets подготовлены, однако manual approval
на GitHub Free для private-репозитория недоступен (см. [release-runbook](docs/operations/release-runbook.md)).
Подробности версионирования — [docs/architecture/adr/0009-android-versioning.md](docs/architecture/adr/0009-android-versioning.md).

## Подпись и секреты

Keystore, пароли и signing secrets **запрещено** хранить в репозитории, истории Git,
логах и артефактах. Ожидаемые имена secrets (значения задаёт владелец репозитория
вручную в GitHub Environment `release`):

- `ANDROID_KEYSTORE_BASE64`
- `ANDROID_KEY_ALIAS`
- `ANDROID_KEY_PASSWORD`
- `ANDROID_KEYSTORE_PASSWORD`

## Third-party licenses

Проект использует сторонние компоненты; лицензии и уведомления — в
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

Media-библиотека **SIPSorcery** принята владельцем в [ADR-0005](docs/architecture/adr/0005-media-transport-library.md):
BSD-3-Clause + Additional Use Restriction; версия зафиксирована `>= 10.0.17` в `Directory.Packages.props`.
Пакет будет подключён к Infrastructure при реализации media-адаптера (feature 003/005).

## Лицензия

MIT — см. [LICENSE](LICENSE).
