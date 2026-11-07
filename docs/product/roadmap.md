# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-11-07
**Текущая фаза**: 001-audio-transport-spike (In Progress; phase 1+2 merged, решение по лицензии SIPSorcery принято владельцем; device matrix BLOCKED — нет реальных устройств)

Допустимые статусы milestone: Proposed, Ready, In Progress, Blocked, Done.
Источники истины: этот файл (milestone status), спецификации в .specify/specs, задачи в tasks.md,
GitHub Issues/PR и CI. Отдельные PROGRESS/STATUS файлы не ведутся.

| Milestone | Status | Goal | Exit criteria | Feature spec | GitHub issue/PR |
|---|---|---|---|---|---|
| 000-bootstrap | Done | Создать воспроизводимый фундамент репозитория и CI/CD | Solution, каркас, docs/ADR, CI (run success), release-by-tag с secrets gate, private repo; environment release + signing secrets + keystore подготовлены | [spec](../../.specify/specs/000-bootstrap/spec.md) | PR #1–#4 merged (7095f75, 5eff3a5, 7afd134, d9b09ad) |
| 001-audio-transport-spike | In Progress | Проверить media-транспорт на device matrix | Phase 1: IRealtimeTransport + contract suite (22 теста) merged; Phase 2: SIPSorcery принят владельцем — **ADR-0005 Accepted (2026-11-07)**, pin ≥10.0.17, THIRD-PARTY-NOTICES; Phase 3 device matrix BLOCKED (нет реальных устройств); media-адаптер — в 003/005 | [spec](../../.specify/specs/001-audio-transport-spike/spec.md) | PR #4 merged (d9b09ad); PR #6 (license docs) |
| 002-device-discovery | In Progress | NSD/DNS-SD advertise/discover/resolve в LAN + понятные ошибки | Phase 1: порты + PeerTxtRecords (TXT только v/role) + fake/contract tests + privacy unit-тесты; Phase 2: Android NSD adapter + permissions + multicast lock + connectivity precondition; emulator smoke install/launch (discovery неприменим без UI), device matrix BLOCKED | [spec](../../.specify/specs/002-device-discovery/spec.md) | PR #8 |
| 003-foreground-monitoring | In Progress | Foreground service для Child (microphone) и Parent (mediaPlayback) с постоянным уведомлением | Контракт IForegroundSessionHost + fake/contract tests; Android service + notification + Stop + FGS-типы + POST_NOTIFICATIONS; Activity visibility/typed errors; device/13+/14+/Doze проверки BLOCKED (нет устройств) | [spec](../../.specify/specs/003-foreground-monitoring/spec.md) | PR (в работе) |
| 004-secure-pairing | In Progress | Pairing token/QR, подтверждение ключей, verification code, trusted store в Keystore | Phase 1: threat model + pairing protocol (ECDSA/ECDH P-256, HKDF code, replay/expiry, user confirm, trusted store port) + unit/security tests; Phase 2: Android Keystore identity + trusted store adapters + architecture tests; end-to-end на устройствах BLOCKED | [spec](../../.specify/specs/004-secure-pairing/spec.md) | PR (phase 1) |
| 005-session-reliability | In Progress | Автоматическое переподключение, health model, локальная тревога Parent | Phase 1: MonitoringSession КА, reconnect policy, SessionHealthMonitor, ParentLossAlarm + unit-тесты (merged); Phase 2 **BLOCKED**: media adapter требует Accepted ADR-0007 signaling/DTLS-SRTP схемы и реальных устройств; SIPSorcery PackageReference ещё не добавлен | [spec](../../.specify/specs/005-session-reliability/spec.md) | PR (phase 1) |
| 006-parent-child-ui | In Progress | Экраны выбора роли, сопряжения, состояния, диагностики; MVVM | Phase 1: ADR-0006 Accepted (.NET for Android), role selection + MonitoringActivity start/stop/status через IForegroundSessionHost + permission bridge; phase 2: pairing UI/QR/diagnostics; media/audio BLOCKED (005 phase 2) | [spec](../../.specify/specs/006-parent-child-ui/spec.md) | PR (phase 1) |
| 007-device-hardening | Proposed | Устойчивость к OEM-ограничениям, audio focus/route, выключенному экрану | Проверка на Pixel/Samsung/Huawei/Xiaomi; 8-часовой soak; профилирование; battery drain | planned | — |
| 008-release-readiness | Proposed | Подготовка первого релиза и signing chain | Постоянный keystore вне репозитория, Environment release + secrets (approval — при доступном плане), security/license scan, release dry-run, CHANGELOG | planned | — |

## Правила статусов

- **Proposed** — есть идея, spec ещё не принята; до готовности spec и ADR не начинается реализация.
- **Ready** — spec/plan/tasks/checklist приняты, зависимости выполнены.
- **In Progress** — работа идёт; задачи отмечаются в tasks.md.
- **Blocked** — внешний блокер; причина фиксируется в отчёте/issue.
- **Done** — exit criteria подтверждены фактическими проверками.

## Ближайший шаг

**002-device-discovery** — phase 1 (контракт + contract suite + TXT privacy) и phase 2 (Android NSD
adapter + permissions + multicast lock) реализованы в PR #8; emulator smoke discovery неприменим без
UI/инструментации, device matrix **BLOCKED: нет реальных устройств**. Phase 2 покрыта build,
architecture tests и review; contract suite для platform-адаптера требует net10.0-android test host
(device tests, BLOCKED).
**001** остаётся In Progress: лицензия SIPSorcery принята владельцем (ADR-0005 Accepted, pin ≥10.0.17,
THIRD-PARTY-NOTICES); device matrix (phase 3) **BLOCKED: нет реальных устройств**; media-адаптер —
в 003/005, не раньше.
