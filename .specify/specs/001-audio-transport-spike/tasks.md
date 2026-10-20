# Tasks: 001-audio-transport-spike

Статусы: [ ] не начато, [-] заблокировано (+причина), [x] завершено и проверено.

## Phase 1 — Контракт и contract suite

- [x] T001 Порт IRealtimeTransport и типы в Application — `src/BabyMonitor.Application/Abstractions/Realtime/`
- [x] T002 Ручной test double + общий contract suite (22 теста: 15 общих contract + 7 fake-specific) — `tests/BabyMonitor.Infrastructure.Tests/Realtime/`
- [x] T003 Добавить Infrastructure.Tests в solution и CI — `BabyMonitor.slnx`, `.github/workflows/ci.yml`
- [x] T004 Обновить architecture tests под 9-й проект (SolutionStructureTests/ProjectReferenceTests) — `tests/BabyMonitor.Architecture.Tests/`
- [x] T005 Contract suite: edge cases (receive after close, cancel, state ordering на fake, double dispose) — `tests/BabyMonitor.Infrastructure.Tests/`

## Phase 2 — Оценка пакета

- [x] T006 Оценка SIPSorcery: лицензия/зависимости/advisories/TFM — в ADR-0005 — `docs/architecture/adr/0005-media-transport-library.md`
- [ ] T006a Размер APK, сборка/trimming/AOT под net10.0-android, SBOM/transitive-license скан — после решения о пакете (phase 2/3)
- [-] T007 Решение о добавлении пакета/экспериментального адаптера — BLOCKED: лицензия SIPSorcery = BSD-3-Clause + Additional Use Restriction (не OSI/NOASSERTION), нужно решение владельца; пакет не добавлен
- [-] T008 Spike harness с реальным кандидатом (loopback/эмулятор) — BLOCKED: зависит от T007 (пакет не одобрен)

## Phase 3 — Device matrix (BLOCKED)

- [-] T009 Выполнить device matrix на 2 реальных устройствах — BLOCKED: реальных Android-устройств нет; emulator не воспроизводит audio latency/CPU/battery
- [ ] T010 Собрать метрики и сравнительную таблицу — после T009

## Verification Log

| Дата | Проверка | Команда/способ | Результат |
|---|---|---|---|
| 2026-10-20 | Build | dotnet build BabyMonitor.slnx -c Debug/Release | 0 warnings, 0 errors |
| 2026-10-20 | Тесты | dotnet test 4 test-проекта (Release, --no-build) | Domain 6, Application 42, Architecture 37, Infrastructure 22 = 107 pass, exit 0 |
| 2026-10-20 | Format | dotnet format --verify-no-changes | exit 0 |
| 2026-10-20 | YAML + bash | PyYAML + bash -n по workflow run-блокам | OK |
| 2026-10-20 | Независимое ревью | reviewer-001 pass 1+2 | CHANGES REQUIRED → PASS WITH COMMENTS; MAJOR 0; CHK 10/10 PASS |
| 2026-10-20 | Signing smoke | local signed release APK постоянным keystore | сертификат не debug, versionCode 100, versionName 0.1.0 |
| 2026-10-20 | Device matrix | phase 3 | **BLOCKED**: нет 2 реальных Android-устройств |

## Phase 4 — Решение

- [ ] T011 Перевести ADR-0005 в Accepted/Rejected по данным — `docs/architecture/adr/0005-media-transport-library.md`
- [ ] T012 PR #2, CI green, независимое ревью — GitHub
