# Release runbook (ручные действия владельца)

Документ фиксирует, что уже настроено и что осталось сделать до первого release.
Секреты и keystore в репозиторий не попадают.

## Уже настроено

- Private repo `Dzirt089/baby-monitor`, default branch `main`.
- Workflows `CI` и `Release` зарегистрированы на default branch; `Dependabot alerts` и
  `automated security fixes` включены; Actions default permissions = read.
- Environment `release` создан.
- Environment secrets `release`: `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEY_ALIAS`,
  `ANDROID_KEY_PASSWORD`, `ANDROID_KEYSTORE_PASSWORD` (значения записаны, в Git/логи не попадают).
- Постоянный release keystore создан вне репозитория, в защищённом хранилище владельца
  (путь, alias и пароли в репозиторий не записываются): RSA 4096, срок 100 лет.
  **Обязательно сделать offline-бэкап keystore и паролей.** Потеря keystore делает обновления
  приложения невозможными.
- Milestone `MVP` создан; labels `type:*`, `area:*`, `status:blocked`, `skip-changelog` созданы.

## Ограничения плана GitHub (выявлены в bootstrap)

На private-репозитории GitHub Free недоступны:
- branch protection / repository rulesets (ответ API: «Upgrade to GitHub Pro or make this
  repository public»);
- required reviewers для environments («billing plan supports the required reviewers...»);
- secret scanning / push protection (нет GitHub Advanced Security).

Поэтому release environment существует, но **без manual approval gate**, а `main` защищён только
процессом (PR + зелёный CI), а не технически. Варианты закрытия:
1. GitHub Pro/Team для private-репозитория — включить rulesets, environment reviewers,
   secret scanning (если доступен в плане).
2. Оставить как есть: релиз инициируется push-ом тега; тег создаёт владелец или делегированный
   агент (только v0.x, см. раздел ниже), CI обязателен по процессу.

## Тестовые сборки v0.x и полномочия агента (2026-11-08)

Владелец делегировал агенту создание тегов `vMAJOR.MINOR.PATCH` и GitHub Releases при условиях
(режим «максимальная автономия с предохранителями»):

- тег создаётся только после merge PR в `main`, зелёных локальных проверок (build Debug/Release,
  все тесты, format, vuln scan) и **зелёного CI** (проверка через `gh` CLI, после `gh auth login`);
- теги не двигаются, force-push запрещён; major-версия (1.0.0+) и удаление тегов/релизов/веток с
  историей — только с явного разрешения владельца;
- signing secrets / Environment `release` / keystore — только владелец; агент **не имеет доступа**
  к значениям secrets. При этом push тега запускает подпись и публикацию Release в CI **без manual
  approval** (GitHub Free), поэтому делегирование ограничено тестовыми сборками v0.x и условиями
  выше; major-версия и production-релиз — только по явному «да» владельца;
- release notes и CHANGELOG обязаны быть честными: никаких заявлений о работающей радионяне, пока
  владелец не подтвердил звук на двух устройствах;
- тестовые сборки v0.x используют постоянный release keystore (тот же сертификат) — это разрешено
  владельцем для side-load по ADR-0008 и обеспечивает обновление «поверх»;
- после каждого релиза — краткий отчёт: что проверено, что BLOCKED, какие device-AC ждут прогона
  владельца, + чек-лист device-тестов.

## Release (v0.x — по делегированию выше; 1.0.0+ — только с явного разрешения владельца)

1. Обновить `CHANGELOG.md`: раздел `## [X.Y.Z]`.
2. Убедиться, что `main` зелёный и PR смержен.
3. Создать тег `vX.Y.Z` на commit в `main` и запушить его: владелец или делегированный агент
   (v0.x; теги не двигаются). Release создаёт workflow, а не агент вручную.
4. Проверить run workflow `Release`: verify → release job (Environment release), подпись
   постоянным keystore, проверка сертификата/versionCode, SHA-256, GitHub Release с APK и checksum.
5. Если secrets отсутствуют — release job падает до публикации (by design).

## Правила

- Никогда не коммитить keystore, пароли, base64 keystore и signing properties.
- Не публиковать debug-signed APK как production Release (workflow это блокирует).
- Не создавать/не двигать теги из workflow; тег создаёт владелец или делегированный агент
  (v0.x по этому runbook), теги не двигаются.
- До первого release проверить ApplicationId `io.github.dzirt089.babymonitor` (ADR-0010).
