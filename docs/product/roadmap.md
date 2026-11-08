# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-11-08
**Текущая фаза**: v0.3.1 подтверждена на двух реальных Huawei (слышимость подтверждена владельцем, 2026-11-08); 007 device hardening — In Progress, 008 release readiness — owner-gated остатки

Допустимые статусы milestone: Proposed, Ready, In Progress, Blocked, Done.
Источники истины: этот файл (milestone status), спецификации в .specify/specs, задачи в tasks.md,
GitHub Issues/PR и CI. Отдельные PROGRESS/STATUS файлы не ведутся.

| Milestone | Status | Goal | Exit criteria | Feature spec | GitHub issue/PR |
|---|---|---|---|---|---|
| 000-bootstrap | Done | Создать воспроизводимый фундамент репозитория и CI/CD | Solution, каркас, docs/ADR, CI (run success), release-by-tag с secrets gate, private repo; environment release + signing secrets + keystore подготовлены | [spec](../../.specify/specs/000-bootstrap/spec.md) | PR #1–#4 merged |
| 001-audio-transport-spike | Done | Проверить media-транспорт на device matrix | IRealtimeTransport + contract suite; ADR-0005 Accepted; SIPSorcery 10.0.17; media-адаптер реализован в 005 и подтверждён на реальных Huawei (v0.3.1: MediaFlowing, owner-confirmed звук). Полная OEM-матрица — 007 | [spec](../../.specify/specs/001-audio-transport-spike/spec.md) | PR #4/#6/#7 merged; v0.3.1 |
| 002-device-discovery | Done | NSD/DNS-SD advertise/discover/resolve в LAN + понятные ошибки | Порты/contract/privacy tests; Android NSD adapter; обнаружение Child↔Parent проверено на реальных Huawei (v0.3.1, оба в одном LAN). Guest Wi-Fi/AP isolation — 007 | [spec](../../.specify/specs/002-device-discovery/spec.md) | PR #8 merged; v0.3.1 |
| 003-foreground-monitoring | Done | Foreground service для Child (microphone) и Parent (mediaPlayback) с постоянным уведомлением | Контракт/сервис/уведомление/Stop/FGS-типы; проверено на Android 12 Huawei: FGS, «Звук передаётся», screen-off 12 c, Stop снимает уведомление/AudioTrack. Android 13+/14+ Doze/permission flows — 007 | [spec](../../.specify/specs/003-foreground-monitoring/spec.md) | PR #9 merged; v0.3.1 |
| 004-secure-pairing | Done | Pairing token/QR, подтверждение ключей, verification code, trusted store в Keystore | Threat model + protocol + Android Keystore/trusted store + tests; end-to-end pairing на двух реальных Huawei: одинаковый code 848016, trusted store обоих, DER/P1363 interop. Camera↔camera на реальных устройствах — 007 (использован QR paste fallback) | [spec](../../.specify/specs/004-secure-pairing/spec.md) | PR #10/#11 merged; v0.3.1 |
| 005-session-reliability | Done | Автоматическое переподключение, health model, локальная тревога Parent | Phase 1: MonitoringSession КА, reconnect policy, SessionHealthMonitor, ParentLossAlarm + unit-тесты (merged); Phase 2: ADR-0007 Accepted; signaling/TLS+pinning/identity assertion, SIPSorcery media/Opus/DTLS-SRTP, Android audio, coordinator+FGS/UI — v0.3.0/0.3.1; device verification 2026-11-08: pairing, **слышимость подтверждена владельцем**, reconnect Wi-Fi, screen-off, Stop; heartbeat/ParentLossAlarm/telemetry — 007 | [spec](../../.specify/specs/005-session-reliability/spec.md) | PR #12/#16 merged; v0.3.0/v0.3.1 |
| 006-parent-child-ui | Done | Экраны выбора роли, сопряжения, состояния, диагностики; MVVM | Phase 1 (merged): ADR-0006 Accepted, role selection, MonitoringActivity start/stop/status, permission bridge; Phase 2: pairing UI/QR (офлайн-обмен QR Child↔Parent), verification code, trusted devices (forget/re-pair), camera scan (ADR-0011 ZXing.Net), diagnostics screen, ViewModels, a11y/l10n; media/audio реализованы в 005; pairing UI на двух реальных Huawei (paste fallback) подтверждён, camera scan — emulator + 007 | [spec](../../.specify/specs/006-parent-child-ui/spec.md) | PR #13/#14 merged; v0.3.1 |
| 007-device-hardening | In Progress | Устойчивость к OEM-ограничениям, audio focus/route, выключенному экрану | Huawei nova Y90+nova 13 Pro (Android 12): pairing/звук(owner)/reconnect/screen-off/Stop — PASS; осталось: heartbeat/ParentLossAlarm/telemetry/LAN hardening, calls/BT/route, 8h soak, 100 циклов, battery без зарядки, TalkBack, camera, OEM-матрица | [spec](../../.specify/specs/007-device-hardening/spec.md) | planned PR; device results 2026-11-08 |
| 008-release-readiness | In Progress | Release readiness и distribution | Side-load цепочка работает (v0.2.0/0.3.0/0.3.1: signed APK+SHA-256, gates); осталось: signing-chain runbook/rotation (owner), license/privacy/disclaimer, distribution decision (owner-gated), incident/rollback docs | [spec](../../.specify/specs/008-release-readiness/spec.md) | v0.2.0/v0.3.0/v0.3.1 releases |

## Правила статусов

- **Proposed** — есть идея, spec ещё не принята; до готовности spec и ADR не начинается реализация.
- **Ready** — spec/plan/tasks/checklist приняты, зависимости выполнены.
- **In Progress** — работа идёт; задачи отмечаются в tasks.md.
- **Blocked** — внешний блокер; причина фиксируется в отчёте/issue.
- **Done** — exit criteria подтверждены фактическими проверками.

## Ближайший шаг

**v0.2.0 / v0.3.0 / v0.3.1** опубликованы как GitHub Releases (signed APK + SHA-256). **v0.3.1
подтверждена на двух реальных устройствах владельца** (Huawei nova Y90 + nova 13 Pro, Android 12):
сопряжение, передача звука (**владелец подтвердил слышимость**), MediaFlowing, reconnect Wi-Fi,
выключенный экран, чистый Stop — PASS; результаты:
docs/operations/device-test-results-2026-11-08.md.

**Дальше — 007** ([spec](../../.specify/specs/007-device-hardening/spec.md)): control heartbeat +
ParentLossAlarm + telemetry counters + LAN hardening (H1/H2/H5), звонки/audio focus, Bluetooth/
наушники, 8h soak, 100 reconnect-циклов, battery без зарядки, TalkBack, camera↔camera, OEM-матрица
(последняя может остаться частичной/BLOCKED без устройств). После 007 — 008
([spec](../../.specify/specs/008-release-readiness/spec.md)): signing-chain runbook, license/privacy/
disclaimer, решение владельца по видимости/Play/major-версии. Публичные действия, secrets/keystore,
удаление тегов/релизов/веток — только по явному «да» владельца.