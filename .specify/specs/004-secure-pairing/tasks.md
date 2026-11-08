# Tasks: 004-secure-pairing

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Контракты и протокол

- [x] T001 Identity/PairedDevice/trusted store/key store/entropy контракты — `src/BabyMonitor.Application/Abstractions/Security/`, `src/BabyMonitor.Application/Pairing/`
- [x] T002 QR payload + подписи + ECDH + verification code — `src/BabyMonitor.Application/Pairing/`
- [x] T003 Replay/expiry/подтверждение пользователем + trusted store state machine — `src/BabyMonitor.Application/Pairing/`
- [x] T004 Unit-тесты протокола и store — `tests/BabyMonitor.Application.Tests/Pairing/`, `tests/BabyMonitor.Infrastructure.Tests/Pairing/`
- [x] T005 Threat model + spec/plan/checklist/roadmap — `docs/security/pairing-threat-model.md`

## Phase 2 — Android adapters

- [x] T006 Android Keystore ECDSA P-256 identity adapter — `src/BabyMonitor.Platform.Android/Security/`
- [x] T007 Android trusted store (только публичные данные) — `src/BabyMonitor.Platform.Android/Security/`
- [x] T008 Architecture tests: Application без Android-типов; private key не в QR/store — `tests/BabyMonitor.Architecture.Tests/`

## Phase 3 — Проверки

- [x] T009 Local gates: build Debug/Release, tests, format
- [x] T010 Emulator smoke: install/launch (pairing UI отсутствует до 006)
- [x] T011 End-to-end pairing двух устройств — PASS 2026-11-08 (paste-fallback, code совпал, trusted store); camera↔camera — 007
- [-] T012 Keystore на OEM/API матрице — **BLOCKED: нет устройств** (007)
- [x] T013 PR → CI → независимое ревью → merge

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Phase 1 build/tests | Domain 6 + Application 115 + Architecture 45 + Infrastructure 63 = 229/229 pass; build 0 warnings; format OK |