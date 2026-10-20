# ADR-0009: Версионирование Android-приложения (versionName/versionCode)

- **Статус**: Accepted
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор
- **Связанные документы**: [release.yml](../../../.github/workflows/release.yml), [Directory.Build.props](../../../Directory.Build.props)

## Контекст

Android требует монотонно растущий integer versionCode для обновлений и human-readable
versionName. Git-теги имеют формат vMAJOR.MINOR.PATCH; release workflow не должен создавать теги,
но обязан собрать артефакт, соответствующий тегу. Формула должна быть детерминированной,
проверяемой до первого release и не зависеть от счётчика CI.

## Решение

- Git tag: vMAJOR.MINOR.PATCH (SemVer, без префиксов/суффиксов; prerelease-суффиксы не
  поддерживаются до отдельного решения).
- versionName (ApplicationDisplayVersion) = MAJOR.MINOR.PATCH.
- versionCode (ApplicationVersion) = MAJOR*10000 + MINOR*100 + PATCH.
- Ограничения формулы: 0 <= MINOR < 100, 0 <= PATCH < 100, MAJOR >= 0; полный versionCode
  должен быть > 0 и <= 2_100_000_000 (Android limit). Нарушение — ошибка Directory.Build.props
  и release workflow.
- Release workflow вычисляет MAJOR/MINOR/PATCH из тега и передаёт в dotnet publish только
  BabyMonitorVersionMajor/BabyMonitorVersionMinor/BabyMonitorVersionPatch; VersionPrefix,
  ApplicationDisplayVersion и BabyMonitorVersionCode/ApplicationVersion выводятся из них
  в Directory.Build.props и BabyMonitor.App.csproj.
- verify-job проверяет SemVer-тег и раздел CHANGELOG и заново собирает/тестирует commit;
  release-job после валидации environment release повторно проверяет тег и собирает подписанный
  APK с версией из тега (manual approval — только при доступном плане GitHub).
- Bootstrap-фаза: 0.1.0 → versionCode 100 (значения по умолчанию в Directory.Build.props).
- Диапазоны MINOR/PATCH и лимит Android проверяются дважды: в Directory.Build.props (любая
  сборка с некорректной версией падает) и в release workflow (понятная ошибка до публикации).
- CHANGELOG.md обязан содержать раздел "## [X.Y.Z]" для выпускаемой версии; workflow падает
  до публикации, если раздела нет.
- Workflow никогда не создаёт/не двигает теги; тег создаётся человеком по инструкции README.

Примеры: 0.1.0 → 100; 1.0.0 → 10000; 1.2.3 → 10203.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Почему отклонена |
|---|---|---|---|
| versionCode из номера CI run | автоматически растёт | не связан с версией, недетерминирован | нарушает соответствие tag↔artifact |
| major*1_000_000+minor*1_000+patch | больше запаса | не нужно для MVP, сложнее читать | выбран компактный вариант |
| Дата как versionCode | монотонно | не отражает SemVer, сложно проверять | непрозрачно |
| Ручной versionCode в файле | просто | рассинхронизация с тегом, человеческие ошибки | риск первого релиза |

## Последствия

- Версия артефакта однозначно выводится из тега; повторный запуск workflow для того же тега даёт
  тот же versionCode.
- При MINOR/PATCH >= 100 требуется новый ADR (или major-перескок) — ограничение сознательное.
- Пока теги не создаются, release workflow доступен в ручном dry-run режиме без production-подписи;
  артефакт dry-run не предназначен для установки как релиз.

## Проверка и критерии пересмотра

Release workflow проверяет regex тега, диапазоны MINOR/PATCH, наличие CHANGELOG-раздела и
сравнивает ожидаемые versionName/versionCode через aapt/apkanalyzer с фактическим APK. Пересмотр —
при переходе на другую схему тегирования или требовании Play (versionCode policy).
