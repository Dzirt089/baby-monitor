# Tasks: 005-session-reliability

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Доменный КА и reliability-политики

- [x] T001 MonitoringState/DisconnectReason/SessionHealth + MonitoringSession aggregate
- [x] T002 ReconnectPolicy (backoff+jitter+cap)
- [x] T003 SessionHealthMonitor (control/media независимо)
- [x] T004 ParentLossAlarm (локальная тревога Parent)
- [x] T005 Unit-тесты КА/переходов/health/backoff/alarm
- [x] T006 005 spec/plan/tasks/checklist/roadmap

## Phase 2 — Media и Android audio

- [ ] T007 SIPSorcery adapter за IRealtimeTransport (>= 10.0.17) + PackageReference в Infrastructure
- [ ] T008 Contract suite для SIPSorcery adapter (общий набор)
- [ ] T009 Android AudioCapture/AudioPlayback adapters + role invariants
- [ ] T010 Use case wiring: reconnect loop + connectivity monitor + telemetry
- [ ] T011 Architecture tests: SIPSorcery не протекает в Application; роль-инварианты audio

## Phase 3 — Проверки

- [x] T012 Local gates: build Debug/Release, tests, format
- [x] T013 Emulator smoke: install/launch
- [-] T014 100 connect/disconnect/reconnect, потеря Wi-Fi, звонки, 8h soak, battery — **BLOCKED: нет реальных устройств**
- [-] T015 Реальная слышимость аудио и media metrics — **BLOCKED: нет устройств**
- [x] T016 PR → CI → независимое ревью → merge

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Phase 1 build/tests | Domain 38/38, Application 147/147, Architecture 45/45, Infrastructure 63/63 = 293/293; build 0 warnings; format OK |