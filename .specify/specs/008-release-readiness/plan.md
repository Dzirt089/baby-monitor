# Implementation Plan: 008 — Release readiness & distribution

**Spec**: [spec.md](spec.md) · **Status**: Ready (side-load релизы уже выпускаются)

## Что уже работает (evidence)

- Release workflow `.github/workflows/release.yml`: SemVer tag → build (warnings-as-errors), tests
  (Domain/Application/Architecture/Infrastructure), vuln scan, signing keystore из Environment secrets,
  publish signed APK, verify signing cert/metadata, SHA-256, GitHub Release; временный keystore
  удаляется. Релизы v0.2.0 (2026-11-08), v0.3.0, v0.3.1 — success; APK+SHA-256 опубликованы.
- ADR-0008: side-load, без auto-update; ADR-0009: versionCode=MAJOR*10000+MINOR*100+PATCH.
- Независимый security review 005 phase 2 (TLS/pinning/media) — APPROVE; CI на PR #15/#16/#17 зелёный.
- Device pass v0.3.1: owner-confirmed звук на Huawei nova Y90 + nova 13 Pro; CHANGELOG/README/
  device results обновлены.

## Оставшиеся работы (owner-gated или docs)

1. **Signing chain/runbook**: зафиксировать, где хранится keystore вне репозитория, как передаётся,
   процедура ротации secrets (владелец), dry-run сборки после ротации, кто владеет Environment
   `release`. Всё — docs/operations/release-runbook.md; без изменения secrets агентом.
2. **Лицензия/правовая часть**: определить лицензию проекта (owner decision), актуализировать
   THIRD-PARTY-NOTICES для публичного распространения, privacy statement (аудио не пишется/не
   отправляется, нет аккаунтов/аналитики, side-load), disclaimer «не медицинское изделие».
3. **Distribution decision**: публичная видимость репозитория/релизов, Play/marketplaces, платные
   сервисы — только владелец; подготовить чек-лист и риски (стор-политики для microphone/FGS,
   privacy policy, подпись, скриншоты, Data Safety), но не выполнять.
4. **Version policy**: 0.x до завершения 007 (OEM/soak/a11y) и решения владельца; 1.0.0 — только
   после exit criteria 007/008; patch для критических фиксов (v0.3.1 — пример).
5. **Incident response/rollback**: как отозвать релиз (владелец), как откатить APK (переустановка
   предыдущего), что делать при утечке token/keystore (ротация владельцем, security notice).
6. **Release checklist**: PR-merged + CI green + review + tag + Release + SHA-256/owner verify +
   device results + CHANGELOG/roadmap обновлены.

## Риски

- Публикация/стор — юридические/политические риски (стор-требования, private data, medical claims)
  → только явное решение владельца; агент не выполняет.
- Смена keystore ломает обновления существующих установок → документировать и не менять без owner.
- Публичные релизы могут раскрыть private-репозиторий/историю — owner decision.

## Критерии готовности

- Release runbook/incident/rollback/privacy/license документы готовы; owner-gated пункты честно
  отмечены; никаких публичных/сторовых действий без «да» владельца.
