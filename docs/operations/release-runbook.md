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
2. Оставить как есть: релиз делает только владелец (нужен push tag), CI обязателен по процессу.

## Первый release (только после решения владельца)

1. Обновить `CHANGELOG.md`: раздел `## [X.Y.Z]`.
2. Убедиться, что `main` зелёный и PR смержен.
3. Создать тег `vX.Y.Z` на commit в `main` и запушить его.
4. Проверить run workflow `Release`: verify → release job (Environment release), подпись
   постоянным keystore, проверка сертификата/versionCode, SHA-256, GitHub Release с APK и checksum.
5. Если secrets отсутствуют — release job падает до публикации (by design).

## Правила

- Никогда не коммитить keystore, пароли, base64 keystore и signing properties.
- Не публиковать debug-signed APK как production Release (workflow это блокирует).
- Не создавать/не двигать теги из workflow; тег создаёт человек.
- До первого release проверить ApplicationId `io.github.dzirt089.babymonitor` (ADR-0010).