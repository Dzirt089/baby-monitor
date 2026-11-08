# Tasks: 008 — Release readiness & distribution

**Статусы**: [ ] pending · [x] done · [-] blocked (owner-only / external)

## Phase 1 — Signing chain / release runbook

- [x] T001 Release workflow: tag → CHANGELOG/secrets validation → build/tests/vuln → signed APK +
  SHA-256 → GitHub Release (v0.2.0/v0.3.0/v0.3.1 success).
- [x] T002 APK hash проверен владельцем/агентом при установке (v0.3.1 SHA-256 совпал).
- [ ] T003 Зафиксировать в release-runbook: где/как хранится keystore вне репозитория, владелец
  Environment/Signing secrets, процедура ротации (owner-only), dry-run после ротации.
- [ ] T004 Owner-gated: ротация secrets/keystore или любые изменения — только по явному «да».
- [ ] T005 Проверить/описать rollback: установка предыдущего релиза, совместимость versionCode,
  предупреждение о смене keystore.

## Phase 2 — Лицензия/privacy/legal

- [ ] T006 Решение владельца по лицензии проекта (MIT/Apache-2.0/ proprietary) — зафиксировать.
- [ ] T007 Актуализировать THIRD-PARTY-NOTICES для публичного распространения (SIPSorcery/
  SIPSorceryMedia.Abstractions BSD-3+restriction, Concentus, BouncyCastle MIT, DnsClient Apache-2.0,
  Makaretu/WebSocketSharp/Microsoft.Extensions MIT, ZXing.Net Apache-2.0).
- [ ] T008 Privacy statement: аудио не записывается/не сохраняется/не отправляется, нет аккаунтов/
  аналитики/рекламы; side-load; данные pairing/trusted store локальны.
- [ ] T009 Disclaimer «не медицинское изделие, не заменяет наблюдение взрослого» в README/релизе.

## Phase 3 — Distribution decision (owner-gated)

- [-] T010 Публичная видимость репозитория/релизов — **BLOCKED: owner-only решение**.
- [-] T011 Google Play/маркеты/платные сервисы — **BLOCKED: owner-only; подготовить чек-лист
  (FGS/microphone policy, Data Safety, privacy policy, скриншоты, подпись) по запросу**.
- [ ] T012 Чек-лист store readiness (справочно): политики microphone/FGS, target API, Data Safety,
  privacy, content rating, медицинский disclaimer; не выполнять без «да».

## Phase 4 — Version/incident/docs

- [ ] T013 Version policy: 0.x до закрытия 007 (OEM/soak/a11y) + owner decision; patch-релизы для
  критических фиксов; 1.0.0 — owner-only после AC 007/008.
- [ ] T014 Incident response: порядок действий при критическом дефекте/утечке, patch-релиз, честное
  уведомление; отзыв релиза/тега — только владелец.
- [ ] T015 Финальный release checklist в runbook + шаблон примечаний к релизу (что проверено/что
  BLOCKED/какие device AC остались).
- [x] T016 Device verification v0.3.1 (owner-confirmed sound на двух Huawei) отражена в
  CHANGELOG/README/device results.
- [ ] T017 Подготовить 008 exit report: что готово, что owner-gated, риски публикации.

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-08 | v0.2.0/v0.3.0/v0.3.1 GitHub Releases | Signed APK + SHA-256 опубликованы; CI/review gates пройдены; v0.3.1 подтверждена на 2 реальных Huawei (owner audibility) |
