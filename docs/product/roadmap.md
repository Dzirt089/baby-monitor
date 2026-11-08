# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-11-08
**Текущая фаза**: 005 phase 2 реализована в коде (PR v0.3.0); device-прогон слышимости/reconnect/battery владельцем на Huawei nova 13 Pro + Huawei nova Y90

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
| 005-session-reliability | In Progress | Автоматическое переподключение, health model, локальная тревога Parent | Phase 1: MonitoringSession КА, reconnect policy, SessionHealthMonitor, ParentLossAlarm + unit-тесты (merged); Phase 2: ADR-0007 Accepted (вариант A); signaling (mutual TLS + pinning/identity assertion), SIPSorcery media (Opus/DTLS-SRTP), Android audio adapters, coordinator + FGS/UI интеграция — реализованы; device-прогоны слышимости/reconnect/battery — владельцем после v0.3.0 | [spec](../../.specify/specs/005-session-reliability/spec.md) | PR #12 merged |
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

**v0.2.0** опубликована как GitHub Release (pairing UI; device-чек-лист —
docs/operations/device-test-checklist-v0.2.0.md). **005 phase 2** реализована в коде (signaling/
media/audio + FGS/UI интеграция) и готовится к v0.3.0; device-прогон слышимости/reconnect/battery —
владельцем на Huawei nova 13 Pro и Huawei nova Y90 (чек-лист v0.3.0), adb пока недоступен.

**Следующий крупный этап — 005 phase 2**: design-ADR-0007 вариант A (mutual TLS + pinning
self-signed ECDSA identity, media — SIPSorcery DTLS-SRTP) и Application-порты аудио/signaling
подготовлены на ветке 005 phase 2; далее реализация signaling/media/AudioRecord/AudioTrack →
v0.3.0 и device-проверка слышимости владельцем.

**Дальше**: 005 phase 2 (media/signaling, ADR-0007 вариант A) разблокируется после merge PR 005
phase 2 (design/порты уже подготовлены); устройства владельца доступны для device-проверок после
v0.3.0. 007/008 требуют результатов device-прогонов и
явного подтверждения владельца по signing/secrets/видимости/публичному релизу; major-версия,
удаление тегов/релизов/веток — только с разрешения владельца.