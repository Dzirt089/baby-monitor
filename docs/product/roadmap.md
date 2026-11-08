# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-11-08
**Текущая фаза**: 006-parent-child-ui (phase 2 в PR #14: pairing UI/QR, diagnostics, ViewModels; device matrix BLOCKED — нет реальных устройств)

Допустимые статусы milestone: Proposed, Ready, In Progress, Blocked, Done.
Источники истины: этот файл (milestone status), спецификации в .specify/specs, задачи в tasks.md,
GitHub Issues/PR и CI. Отдельные PROGRESS/STATUS файлы не ведутся.

| Milestone | Status | Goal | Exit criteria | Feature spec | GitHub issue/PR |
|---|---|---|---|---|---|
| 000-bootstrap | Done | Создать воспроизводимый фундамент репозитория и CI/CD | Solution, каркас, docs/ADR, CI (run success), release-by-tag с secrets gate, private repo; environment release + signing secrets + keystore подготовлены | [spec](../../.specify/specs/000-bootstrap/spec.md) | PR #1–#4 merged |
| 001-audio-transport-spike | In Progress | Проверить media-транспорт на device matrix | Phase 1: IRealtimeTransport + contract suite merged; Phase 2: ADR-0005 Accepted, pin ≥10.0.17, THIRD-PARTY-NOTICES; Phase 3 device matrix **BLOCKED** (нет реальных устройств); media-адаптер — в 005 | [spec](../../.specify/specs/001-audio-transport-spike/spec.md) | PR #4/#6/#7 merged |
| 002-device-discovery | In Progress | NSD/DNS-SD advertise/discover/resolve в LAN + понятные ошибки | Phase 1: порты + PeerTxtRecords + contract/privacy tests; Phase 2: Android NSD adapter + permissions + multicast lock; emulator smoke install/launch; device matrix **BLOCKED** | [spec](../../.specify/specs/002-device-discovery/spec.md) | PR #8 merged |
| 003-foreground-monitoring | In Progress | Foreground service для Child (microphone) и Parent (mediaPlayback) с постоянным уведомлением | Контракт IForegroundSessionHost + contract tests; Android service + notification + Stop + FGS-типы + POST_NOTIFICATIONS; Activity visibility/typed errors; device/13+/14+/Doze **BLOCKED** | [spec](../../.specify/specs/003-foreground-monitoring/spec.md) | PR #9 merged |
| 004-secure-pairing | In Progress | Pairing token/QR, подтверждение ключей, verification code, trusted store в Keystore | Phase 1: threat model + pairing protocol (ECDSA/ECDH P-256, HKDF code, replay/expiry, user confirm, trusted store port) + tests; Phase 2: Android Keystore identity + trusted store adapters + architecture tests; end-to-end на устройствах **BLOCKED**; interop-фикс DER/P1363 в 006 phase 2 | [spec](../../.specify/specs/004-secure-pairing/spec.md) | PR #10/#11 merged |
| 005-session-reliability | In Progress | Автоматическое переподключение, health model, локальная тревога Parent | Phase 1: MonitoringSession КА, reconnect policy, SessionHealthMonitor, ParentLossAlarm + unit-тесты (merged); Phase 2 **BLOCKED**: ADR-0007 signaling/DTLS-SRTP Proposed, media adapter не реализован, нет устройств | [spec](../../.specify/specs/005-session-reliability/spec.md) | PR #12 merged |
| 006-parent-child-ui | In Progress | Экраны выбора роли, сопряжения, состояния, диагностики; MVVM | Phase 1 (merged): ADR-0006 Accepted, role selection, MonitoringActivity start/stop/status, permission bridge; Phase 2: pairing UI/QR (офлайн-обмен QR Child↔Parent), verification code, trusted devices (forget/re-pair), camera scan (ADR-0011 ZXing.Net), diagnostics screen, ViewModels, a11y/l10n; media/audio BLOCKED (005 phase 2); device matrix **BLOCKED** | [spec](../../.specify/specs/006-parent-child-ui/spec.md) | PR #13 merged; PR #14 (phase 2) |
| 007-device-hardening | Proposed | Устойчивость к OEM-ограничениям, audio focus/route, выключенному экрану | Проверка на Pixel/Samsung/Huawei/Xiaomi; 8-часовой soak; профилирование; battery drain; camera/OEM/TalkBack matrix | planned | — |
| 008-release-readiness | Proposed | Подготовка первого релиза и signing chain | Постоянный keystore вне репозитория, Environment release + secrets, security/license scan, release dry-run, CHANGELOG | planned | — |

## Правила статусов

- **Proposed** — есть идея, spec ещё не принята; до готовности spec и ADR не начинается реализация.
- **Ready** — spec/plan/tasks/checklist приняты, зависимости выполнены.
- **In Progress** — работа идёт; задачи отмечаются в tasks.md.
- **Blocked** — внешний блокер; причина фиксируется в отчёте/issue.
- **Done** — exit criteria подтверждены фактическими проверками.

## Ближайший шаг

**006 phase 2** реализована в PR #14; независимое ревью закрыто. Готовится **v0.2.0** — тестовая
сборка для device-прогонов на двух устройствах владельца (pairing/камера/FGS/диагностика;
чек-лист — docs/operations/device-test-checklist-v0.2.0.md). Реальный QR-обмен камера↔камера,
camera/OEM/TalkBack матрица закрываются по результатам прогона (007).

**Следующий крупный этап — 005 phase 2**: ADR-0007 Accepted (вариант A: mutual TLS + pinning
self-signed ECDSA identity, media — SIPSorcery DTLS-SRTP), signaling + AudioRecord/AudioTrack +
реальная передача звука → v0.3.0 и device-проверка слышимости владельцем.

**Дальше**: 007-device-hardening и 008-release-readiness требуют реальных устройств и явного
подтверждения владельца по signing/secrets/видимости репозитория; 005 phase 2 (media/signaling,
ADR-0007) остаётся **BLOCKED** — без Accepted ADR-0007 и устройств не начинается.