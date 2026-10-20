# Implementation Plan: 000-bootstrap

**Spec**: [spec.md](./spec.md) | **Status**: In Progress | **Created**: 2026-10-20

## Summary

Создать локальный репозиторий baby-monitor с каркасом Clean Architecture на .NET 10 Android,
документацией Spec-Driven Development, CI и release workflow, затем создать private GitHub
repository, запушить feature branch и открыть bootstrap PR. Реализацию мониторинга не начинать.

## Technical Context

- SDK: .NET 10.0.400 (global.json, rollForward latestFeature).
- TFM: net10.0 (Domain, Application, Infrastructure, tests); net10.0-android (Platform.Android, App).
- UI: пока чистый .NET for Android (Activity + XML), решение о MAUI остаётся Proposed ADR-0006.
- Tests: xUnit + Microsoft.NET.Test.Sdk + coverlet.collector (только тестовые зависимости).
- Android SDK: platform android-36, build-tools 36.0.0, JDK 17+ (CI Temurin 21), ANDROID_HOME.
- CI: GitHub Actions, ubuntu-latest, actions pinned by commit SHA.
- Release: SemVer tag → dotnet publish APK → подпись только с secrets Environment release.
- Versioning: versionName = MAJOR.MINOR.PATCH; versionCode = MAJOR*10000+MINOR*100+PATCH (ADR-0009).

## Constitution Check

- Принцип I (privacy/security): bootstrap не добавляет сеть/аудио/разрешения; manifest пуст;
  release secrets не создаются, keystore не генерируется.
- Принцип II (роли): UI не реализует роли; домен фиксирует Complement-инвариант.
- Принцип III (зависимости): проверяется architecture tests.
- Принцип IV (надёжность): N/A в bootstrap; DI/аудио не добавляются заранее.
- Принцип V (честные проверки): spec/tasks отражают фактические результаты; CI run проверяется.
- Принцип VI (security-sensitive changes): signing/release спроектирован без секретов в PR.

## Project Structure

```text
baby-monitor/
├─ .github/{workflows,dependabot.yml,release.yml,ISSUE_TEMPLATE,PULL_REQUEST_TEMPLATE.md}
├─ .specify/{memory/constitution.md,specs/000-bootstrap}
├─ docs/{product,architecture/adr}
├─ src/{Domain,Application,Infrastructure,Platform.Android,App}
├─ tests/{Domain.Tests,Application.Tests,Architecture.Tests}
├─ BabyMonitor.slnx
├─ Directory.Build.props, Directory.Packages.props, global.json
└─ README.md, AGENTS.md, CHANGELOG.md, LICENSE, .editorconfig, .gitignore, .gitattributes
```

## Implementation Phases

1. **Локальный каркас**: проекты, references, минимальные контракты, App shell, сборка.
2. **Документация**: constitution, spec/plan/tasks/checklist, vision, roadmap, architecture, ADR.
3. **Тесты и quality gates**: domain/application/architecture tests, локальный прогон.
4. **CI/CD**: ci.yml, release.yml, dependabot, release notes, issue/PR templates.
5. **Независимое ревью**: reviewer pass 1 (каркас) и pass 2 (docs/workflows/tests), исправления.
6. **Git/GitHub**: initial commit в main, feature branch, private repo, push, PR, проверка CI run.
7. **Отчёт**: DoD, риски, ручной checklist настроек GitHub, следующий шаг 001-audio-transport-spike.

## Risk Register

| Риск | Вероятность | Влияние | Митигация |
|---|---|---|---|
| Android workload/SDK недоступны в CI | Средняя | Высокое | ubuntu-latest + dotnet workload install android; локально проверено |
| Cyrillic path ломает Android tooling | Низкая | Высокое | Локально проверено: Debug/Release build успешны |
| Actions pinned SHA устареют | Средняя | Низкое | Dependabot github-actions weekly |
| Release workflow ошибочно опубликует debug APK | Низкая | Высокое | Отдельные job/artifacts; релиз только по тегу + secrets check |
| CI зелёный, но workflow не запускался | — | Высокое | После push читать Actions API и фиксировать фактический статус |
| Случайный коммит секрета/keystore | Низкая | Критическое | .gitignore, secret scan перед push (grep), отсутствие secrets в repo |
| Расхождение docs и реальности | Средняя | Среднее | Reviewer pass 2 + README-команды реально выполняются |

## Phase Gates

- Gate 1: restore/build/test зелёные, APK собирается.
- Gate 2: reviewer PASS/PASS WITH COMMENTS; все BLOCKER/MAJOR устранены.
- Gate 3: secret/path/artifact scan чистый; git status ожидаемый.
- Gate 4: GitHub repo + PR созданы; CI run прочитан и зафиксирован.
