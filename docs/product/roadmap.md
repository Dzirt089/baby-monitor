# Roadmap: Радионяня (Baby Monitor)

**Обновлено**: 2026-10-20
**Текущая фаза**: 000-bootstrap (In Progress)

Допустимые статусы milestone: Proposed, Ready, In Progress, Blocked, Done.
Источники истины: этот файл (milestone status), спецификации в .specify/specs, задачи в tasks.md,
GitHub Issues/PR и CI. Отдельные PROGRESS/STATUS файлы не ведутся.

| Milestone | Status | Goal | Exit criteria | Feature spec | GitHub issue/PR |
|---|---|---|---|---|---|
| 000-bootstrap | In Progress | Создать воспроизводимый фундамент репозитория и CI/CD | Solution, каркас, docs/ADR, тесты, CI, release-by-tag с secrets gate, private repo + PR; локальные проверки зелёные | [spec](../../.specify/specs/000-bootstrap/spec.md) | PR bootstrap/initial-foundation |
| 001-audio-transport-spike | Proposed | Проверить media-транспорт (SIPSorcery vs thin libwebrtc) на device matrix | ADR-0005 Accepted/Rejected с данными latency/CPU/battery/стабильности; IRealtimeTransport contract tests; решение о библиотеке | planned | — |
| 002-device-discovery | Proposed | NSD/DNS-SD advertise/discover/resolve в LAN + понятные ошибки | Два устройства находят друг друга в одной сети; учитываются guest/AP isolation/VLAN; TXT без приватных данных | planned | — |
| 003-foreground-monitoring | Proposed | Foreground service для Child (microphone) и Parent (mediaPlayback) с постоянным уведомлением | Service запускается явным действием, переживает уничтожение Activity, Stop в уведомлении, минимальные permissions, Android 13+/14+ проверены | planned | — |
| 004-secure-pairing | Proposed | Pairing token/QR, подтверждение ключей, verification code, trusted store в Keystore | Threat model + replay/expiration тесты; повторные подключения только от доверенного peer; нет cleartext signaling | planned | — |
| 005-session-reliability | Proposed | Автоматическое переподключение, health model, локальная тревога Parent | 100 циклов connect/disconnect/reconnect; backoff/jitter с fake TimeProvider; тревога независима от Child; метрики | planned | — |
| 006-parent-child-ui | Proposed | Экраны выбора роли, сопряжения, состояния, диагностики; MVVM | ViewModel только через Application use cases; UI-тесты на эмуляторе; инварианты ролей видны пользователю | planned | — |
| 007-device-hardening | Proposed | Устойчивость к OEM-ограничениям, audio focus/route, выключенному экрану | Проверка на Pixel/Samsung/Huawei/Xiaomi; 8-часовой soak; профилирование; battery drain | planned | — |
| 008-release-readiness | Proposed | Подготовка первого релиза и signing chain | Постоянный keystore вне репозитория, Environment release с approval, secrets, security/license scan, release dry-run, CHANGELOG | planned | — |

## Правила статусов

- **Proposed** — есть идея, spec ещё не принята; до готовности spec и ADR не начинается реализация.
- **Ready** — spec/plan/tasks/checklist приняты, зависимости выполнены.
- **In Progress** — работа идёт; задачи отмечаются в tasks.md.
- **Blocked** — внешний блокер; причина фиксируется в отчёте/issue.
- **Done** — exit criteria подтверждены фактическими проверками.

## Ближайший шаг

После merge bootstrap PR (отдельное подтверждение владельца) — **001-audio-transport-spike**:
ADR-0005 с планом spike, контракт IRealtimeTransport и проверка SIPSorcery на реальных устройствах
в одной LAN. До принятия ADR media-код не пишется.
