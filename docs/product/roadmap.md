# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-10-20
**Текущая фаза**: 001-audio-transport-spike (In Progress; phase 1 done, device matrix BLOCKED — нет реальных устройств)

Допустимые статусы milestone: Proposed, Ready, In Progress, Blocked, Done.
Источники истины: этот файл (milestone status), спецификации в .specify/specs, задачи в tasks.md,
GitHub Issues/PR и CI. Отдельные PROGRESS/STATUS файлы не ведутся.

| Milestone | Status | Goal | Exit criteria | Feature spec | GitHub issue/PR |
|---|---|---|---|---|---|
| 000-bootstrap | Done | Создать воспроизводимый фундамент репозитория и CI/CD | Solution, каркас, docs/ADR, CI (run success), release-by-tag с secrets gate, private repo + PR #1 merged (7095f75); environment release + signing secrets + keystore подготовлены | [spec](../../.specify/specs/000-bootstrap/spec.md) | PR #1 merged |
| 001-audio-transport-spike | In Progress | Проверить media-транспорт (SIPSorcery vs thin libwebrtc) на device matrix | Phase 1: IRealtimeTransport + contract suite (22 теста: 15 общих contract + 7 fake-specific) в CI; Phase 2: оценка SIPSorcery выполнена — adoption BLOCKED лицензией (NOASSERTION + use restriction); Phase 3 device matrix BLOCKED (нет реальных устройств); ADR-0005 ожидает решения владельца | [spec](../../.specify/specs/001-audio-transport-spike/spec.md) | PR #4 (open) |
| 002-device-discovery | Proposed | NSD/DNS-SD advertise/discover/resolve в LAN + понятные ошибки | Два устройства находят друг друга в одной сети; учитываются guest/AP isolation/VLAN; TXT без приватных данных | planned | — |
| 003-foreground-monitoring | Proposed | Foreground service для Child (microphone) и Parent (mediaPlayback) с постоянным уведомлением | Service запускается явным действием, переживает уничтожение Activity, Stop в уведомлении, минимальные permissions, Android 13+/14+ проверены | planned | — |
| 004-secure-pairing | Proposed | Pairing token/QR, подтверждение ключей, verification code, trusted store в Keystore | Threat model + replay/expiration тесты; повторные подключения только от доверенного peer; нет cleartext signaling | planned | — |
| 005-session-reliability | Proposed | Автоматическое переподключение, health model, локальная тревога Parent | 100 циклов connect/disconnect/reconnect; backoff/jitter с fake TimeProvider; тревога независима от Child; метрики | planned | — |
| 006-parent-child-ui | Proposed | Экраны выбора роли, сопряжения, состояния, диагностики; MVVM | ViewModel только через Application use cases; UI-тесты на эмуляторе; инварианты ролей видны пользователю | planned | — |
| 007-device-hardening | Proposed | Устойчивость к OEM-ограничениям, audio focus/route, выключенному экрану | Проверка на Pixel/Samsung/Huawei/Xiaomi; 8-часовой soak; профилирование; battery drain | planned | — |
| 008-release-readiness | Proposed | Подготовка первого релиза и signing chain | Постоянный keystore вне репозитория, Environment release + secrets (approval — при доступном плане), security/license scan, release dry-run, CHANGELOG | planned | — |

## Правила статусов

- **Proposed** — есть идея, spec ещё не принята; до готовности spec и ADR не начинается реализация.
- **Ready** — spec/plan/tasks/checklist приняты, зависимости выполнены.
- **In Progress** — работа идёт; задачи отмечаются в tasks.md.
- **Blocked** — внешний блокер; причина фиксируется в отчёте/issue.
- **Done** — exit criteria подтверждены фактическими проверками.

## Ближайший шаг

**001-audio-transport-spike** — phase 1 (контракт + contract suite) выполнена в PR #2; phase 2
(оценка SIPSorcery) выполнена: пакет технически совместим, но лицензия BSD-3-Clause + Additional
Use Restriction (NOASSERTION) блокирует adoption до решения владельца. Phase 3 (device matrix на
2 реальных телефонах) заблокирована отсутствием устройств; emulator не валиден для audio-метрик.
Phase 4 (ADR-0005 Accepted) — после решения владельца и/или device matrix.
