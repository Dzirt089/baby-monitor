# Feature Specification: 009 — App auto-update (GitHub Releases)

**Feature branch**: 009-app-auto-update
**Status**: In Progress (owner requested 2026-11-08; repo made public by owner)
**Created**: 2026-11-08
**Зависит от**: 003 FGS/UI, 006 UI, 008 distribution decision (репозиторий публичный по решению владельца), ADR-0012.

## Контекст

Тестовые сборки распространяются как GitHub Releases (подписанный APK + SHA-256). Владелец хочет:
проверка обновления по кнопке и при запуске, сверка версии/SHA-256/подписи APK с установленной,
скачивание и установка через PackageInstaller (пользователь подтверждает), Wi-Fi-only, проверка раз
в сутки, отключаемо. Репозиторий переведён в public явным решением владельца, поэтому GitHub Releases
доступны без токена. Вшивать GitHub token в APK запрещено.

## Scope

1. Источник обновлений: манифест update-manifest.json, прикладываемый к каждому GitHub Release;
   URL: https://github.com/Dzirt089/baby-monitor/releases/latest/download/update-manifest.json
   (APK — соседний asset releases/latest/download/<apkAssetName>).
2. Application use case: проверка (ручная/авто), сравнение versionCode, фильтр Wi-Fi, суточный throttle,
   включение/выключение авто-проверки (persisted), загрузка + проверка SHA-256/size + установка.
3. Infrastructure: HTTPS GET манифеста (System.Text.Json, schema validation), download APK в cache,
   SHA-256/size verification; typed errors; no secrets.
4. Platform.Android: PackageInstaller session + подтверждение пользователем (при STATUS_PENDING_USER_ACTION
   приложение обязано запустить системный EXTRA_INTENT); проверка подписи APK (SHA-256 signing certificates)
   совпадает с установленной; REQUEST_INSTALL_PACKAGES; unknown-sources flow; отказ при mismatch/downgrade.
   FileProvider не используется (session copy).
5. UI: экран «Обновления» с текущей/последней версией, кнопкой «Проверить обновления», статусом,
   кнопкой «Скачать и установить», чекбоксом «Проверять автоматически (раз в сутки, только Wi-Fi)».
   Авто-проверка при запуске (не чаще раза в сутки, Wi-Fi) с диалогом «Доступно обновление».
6. Release workflow: генерировать и прикладывать update-manifest.json к GitHub Release
   (packageId, versionName, versionCode, apkAssetName, sha256, sizeBytes, notes, publishedAt).

## Requirements

- **FR-001**: По кнопке «Проверить обновления» приложение запрашивает манифест и сообщает: актуальная
  версия / доступна версия X / typed ошибку (нет Wi-Fi, сеть, manifest invalid, packageId mismatch,
  downgrade/equal, hash mismatch, signature mismatch).
- **FR-002**: При запуске приложения (если авто-проверка включена) выполняется не более одной проверки
  за 24 часа; проверка не блокирует UI и не мешает мониторингу; при отсутствии Wi-Fi авто-проверка
  пропускается.
- **FR-003**: Авто-проверку можно отключить чекбоксом; настройка сохраняется между запусками.
- **FR-004**: Скачивание APK разрешено только по Wi-Fi; размер и SHA-256 сверяются с манифестом; при
  несовпадении установка не начинается, файл удаляется, typed ошибка.
- **FR-005**: Перед установкой проверяется, что signing certificate SHA-256 APK совпадает с
  установленным приложением, packageId совпадает, versionCode больше установленного; иначе отказ.
- **FR-006**: Установка — только через PackageInstaller с обязательным подтверждением пользователя;
  silent/background install запрещён. Если «установка из неизвестных источников» запрещена — показать
  экран настроек с объяснением.
- **FR-007**: Никаких токенов/секретов в APK; только HTTPS (system trust), без cleartext; манифест и
  APK не содержат PII; сетевые запросы не передают device ID.
- **FR-008**: Manifest schemaVersion=1; неизвестная schemaVersion → typed unsupported error.
- **FR-009**: Все ошибки типизированы (Result), UI показывает понятное сообщение; логи без секретов.
- **FR-010**: Unit-тесты Application/Infrastructure: сравнение версий, throttle 24h, disable, Wi-Fi-only,
  malformed manifest, hash/size mismatch, unsupported schema, downgrade; architecture test не нарушен.
- **FR-011**: Device-проверка: v0.5.0 → публикация v0.5.1 → кнопка/авто-проверка находит обновление,
  скачивает, SHA/signature OK, открывает PackageInstaller, установка подтверждается, после установки
  версия 0.5.1; отключаемая авто-проверка и Wi-Fi-only проверены.

## Non-goals

- Silent/background install, обновление без пользователя.
- Play In-App Updates (008/Play).
- Собственный сервер обновлений, CDN, аналитика.
- Автообновление без подписи/SHA-256 или с отключённой TLS-проверкой.

## Acceptance

- Все FR реализованы, unit/contract tests зелёные, независимое review APPROVE, CI зелёный.
- GitHub Release v0.5.x содержит update-manifest.json и подписанный APK; update flow проверен на
  реальных Huawei (оба).
- CHANGELOG/README/ADR/roadmap обновлены; release notes честные.