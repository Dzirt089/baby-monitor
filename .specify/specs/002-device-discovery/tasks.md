# Tasks: 002-device-discovery

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Контракт и contract suite

- [x] T001 Порты IPeerDiscovery/IPeerAdvertiser/ILocalNetworkPermission и типы — `src/BabyMonitor.Application/Abstractions/Discovery/`
- [x] T002 PeerTxtRecords (allowed keys v/role) + privacy unit-тесты — `src/BabyMonitor.Application/Abstractions/Discovery/`, `tests/BabyMonitor.Application.Tests/Discovery/`
- [x] T003 Fake-реализации + contract suite discovery/advertiser — `tests/BabyMonitor.Infrastructure.Tests/Discovery/`
- [x] T004 Прогон 4 test-проектов + CI на PR — `.github/workflows/ci.yml`

## Phase 2 — Android NSD adapter

- [x] T005 NSD discover/resolve adapter (API 34+ ServiceInfoCallback, <34 legacy ResolveService) — `src/BabyMonitor.Platform.Android/Discovery/`
- [x] T006 NSD advertise adapter + multicast lock — `src/BabyMonitor.Platform.Android/Discovery/`
- [x] T007 Permission adapter (ACCESS_LOCAL_NETWORK) + manifest permissions — `src/BabyMonitor.Platform.Android/Permissions/`, `AndroidManifest.xml`
- [x] T008 Architecture tests: manifest permissions/no microphone/isolation platform-типов — `tests/BabyMonitor.Architecture.Tests/`

## Phase 3 — Проверки

- [x] T009 Local gates: build Debug/Release, все test-проекты, format — `BabyMonitor.slnx`
- [-] T010 Emulator smoke advertise/discover — НЕПРИМЕНИМО без UI/инструментации: APK install/launch smoke выполнен, но discovery нельзя запустить из приложения; покрыть в 003/006
- [-] T011 Device matrix (2 устройства, guest/AP isolation, VLAN, смена IP) — BLOCKED: нет реальных устройств
- [ ] T012 PR → CI → независимое ревью → merge — GitHub

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Build + tests phase 1 | Application 68/68, Infrastructure 37/37, 0 warnings |
| 2026-11-07 | Build + tests phase 2 | build Debug/Release 0 warnings; Domain 6, Application 68, Architecture 40, Infrastructure 37 = 151/151 pass; format exit 0 |
| 2026-11-07 | Emulator smoke | APK install/launch на AVD API 35; discovery не запускается без UI/инструментации — задокументировано как неприменимое |
| 2026-11-07 | Device matrix | **BLOCKED**: нет реальных Android-устройств |