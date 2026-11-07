# Tasks: 002-device-discovery

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Контракт и contract suite

- [x] T001 Порты IPeerDiscovery/IPeerAdvertiser/ILocalNetworkPermission и типы — `src/BabyMonitor.Application/Abstractions/Discovery/`
- [x] T002 PeerTxtRecords (allowed keys v/role) + privacy unit-тесты — `src/BabyMonitor.Application/Abstractions/Discovery/`, `tests/BabyMonitor.Application.Tests/Discovery/`
- [x] T003 Fake-реализации + contract suite discovery/advertiser — `tests/BabyMonitor.Infrastructure.Tests/Discovery/`
- [ ] T004 Прогон 4 test-проектов + CI на PR — `.github/workflows/ci.yml`

## Phase 2 — Android NSD adapter

- [ ] T005 NSD discover/resolve adapter — `src/BabyMonitor.Platform.Android/Discovery/`
- [ ] T006 NSD advertise adapter + multicast lock — `src/BabyMonitor.Platform.Android/Discovery/`
- [ ] T007 Permission adapter (ACCESS_LOCAL_NETWORK) + manifest permissions — `src/BabyMonitor.Platform.Android/Permissions/`, `AndroidManifest.xml`
- [ ] T008 Architecture/PR review: platform-типы не протекают в Application — `tests/BabyMonitor.Architecture.Tests/`

## Phase 3 — Проверки

- [ ] T009 Local gates: build Debug/Release, все test-проекты, format — `BabyMonitor.slnx`
- [ ] T010 Emulator smoke advertise/discover (или документировать неприменимость) — AVD API 35
- [-] T011 Device matrix (2 устройства, guest/AP isolation, VLAN, смена IP) — BLOCKED: нет реальных устройств
- [ ] T012 PR → CI → независимое ревью → merge — GitHub

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Build + tests phase 1 | Application 68/68, Infrastructure 37/37, 0 warnings |