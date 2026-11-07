# Tasks: 006-parent-child-ui

- [x] T001 ADR-0006 Accepted: .NET for Android (Activity+XML)
- [x] T002 PermissionActivity (IPermissionRequestHost + IActivityVisibility)
- [x] T003 MainActivity: выбор роли Child/Parent
- [x] T004 MonitoringActivity: start/stop/status/diagnostics через IForegroundSessionHost
- [x] T005 Layouts/strings (role + monitoring), typed errors
- [x] T006 Emulator runtime smoke: role selection → Child → Start → FOREGROUND service + notification (Радионяня: ребёнок / Мониторинг активен, ONGOING|FOREGROUND_SERVICE, action=Stop) → Stop → UI «Остановлен»; crash buffer пуст
- [ ] T007 Phase 2: pairing UI/QR/verification code, diagnostics screen, ViewModels
- [-] T008 Device matrix: 12+/13+/14+ permission flows, a11y/TalkBack, OEM — **BLOCKED: нет устройств**
- [-] T009 Media/audio реальная передача звука — **BLOCKED: 005 phase 2 (ADR-0007 Proposed + нет устройств)**
- [ ] T010 Local gates + PR → CI → review → merge

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Emulator runtime smoke (AVD API 35) | role selection OK; Child Start → foreground service + notification OK; Stop → UI Stopped; crash buffer пуст |