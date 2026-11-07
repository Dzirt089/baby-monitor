# Feature Specification: 005-session-reliability

**Feature Branch**: 005-session-reliability
**Created**: 2026-11-07
**Status**: In Progress (phase 1: доменный КА + reconnect/health/alarm; phase 2: SIPSorcery media adapter, Android audio, wiring)
**Input**: AGENTS.md разделы 2, 6, 7, 9, 11, 15; roadmap 005; ADR-0005 (SIPSorcery принят)

## Контекст

Надёжность сеанса важнее минимальной задержки: после кратковременного разрыва приложение обязано
автоматически переподключиться, а Parent — локально поднять тревогу при потере control channel или
media flow, не полагаясь на ответ Child. «Transport connected» не равно «пользователь слышит звук»:
control и media проверяются независимо. Phase 1 реализует доменный КА, политику backoff,
health-монитор и локальную тревогу; phase 2 — media transport adapter (SIPSorcery) и Android audio.

## User Scenarios

### US-1: Автоматическое переподключение (P1)

После потери heartbeat/media или transport error сеанс переходит Degraded → Reconnecting,
backoff-задержки растут с jitter и ограничены сверху; при исчерпании попыток — Failed с typed
причиной. Ручной Stop немедленно прекращает reconnect loop (Stopping → Idle).

### US-2: Parent узнаёт о потере связи (P1)

Parent поднимает локальную тревогу, если peer не подаёт признаков жизни дольше порога. Тревога
не зависит от Child: её вычисляет Parent по локальному времени/таймеру, и она снимается после
восстановления.

### US-3: Различимые состояния здоровья (P1)

Приложение различает потерю heartbeat, остановку media flow, transport error и ручную остановку;
SessionHealth показывает control/media/capture/playback по отдельности.

## Functional Requirements

- **FR-001**: Domain aggregate MonitoringSession — единственный КА состояний (Idle, Discovering,
  Advertising, Pairing, Connecting, Connected, MediaFlowing, Degraded, Reconnecting, Failed,
  Stopping); переходы валидируются, терминальные состояния не перезапускаются, роли дополняют друг
  друга.
- **FR-002**: DisconnectReason типизирован; HeartbeatTimeout и MediaFlowLost — разные причины;
  None/ManualStop не используются как причина деградации/ошибки.
- **FR-003**: SessionHealth immutable: control (Unknown/Connected/Degraded/Lost), media
  (Inactive/Flowing/Stalled), capture/playback; Child только capture, Parent только playback;
  IsUserHearingAudio = control Connected + media Flowing.
- **FR-004**: ReconnectPolicy — один объект конфигурации: exponential backoff, детерминированный
  jitter, верхний предел, максимум попыток; задержки вычисляются по номеру попытки; sleep выполняет
  вызывающий слой с TimeProvider.
- **FR-005**: SessionHealthMonitor — независимые проверки heartbeat и media по TimeProvider;
  Degraded/Lost для control и Flowing/Stalled для media; Reset перед reconnect.
- **FR-006**: ParentLossAlarm — локальная тревога Parent по порогу без ответа Child; поднятие/сброс
  событием; до первой подтверждённой жизни peer не поднимается.
- **FR-007**: Ручной stop отменяет reconnect loop (Stopping → Idle терминален); после Failed сессия
  не возвращается в Connected.
- **FR-008**: Media adapter: SIPSorcery (>= 10.0.17) только в Infrastructure за IRealtimeTransport,
  проходит общий contract suite; Android audio adapters (AudioRecord/AudioTrack) — в
  Platform.Android; Parent не создаёт capture, Child не создаёт playback; DTLS-SRTP (phase 2).
- **FR-009**: Аудио и секреты не логируются/не сохраняются; reconnect/health события — только
  counters/диагностика без payload.
- **FR-010**: Метрики: reconnect count/duration, media silence duration, buffer depth — phase 2;
  телеметрия без аудио.

## Non-Functional Requirements

- **NFR-001**: Тесты deterministic через fake TimeProvider/BCP (без sleep).
- **NFR-002**: Application/Domain без Android/WebRTC-типов; SIPSorcery не протекает в Application.
- **NFR-003**: Device-проверки (100 циклов reconnect, 8h soak, потеря Wi-Fi, звонки) —
  **BLOCKED: нет реальных устройств** (007).

## Acceptance Criteria

- [x] AC-001: Domain КА + переходы/инварианты покрыты unit-тестами.
- [x] AC-002: ReconnectPolicy (рост, cap, jitter, детерминизм) покрыт тестами.
- [x] AC-003: Health monitor и ParentLossAlarm покрыты тестами с fake TimeProvider.
- [ ] AC-004: SIPSorcery adapter компилируется в Infrastructure, проходит contract suite; Application
  не зависит от SIPSorcery (phase 2).
- [ ] AC-005: Android audio adapters компилируются; role invariants; device-путь BLOCKED (phase 2).
- [-] AC-006: 100 connect/disconnect/reconnect, 8h soak, loss of Wi-Fi/calls/OEM — **BLOCKED: нет
  реальных устройств** (007).
- [-] AC-007: Реальная слышимость аудио/media metrics на устройствах — **BLOCKED**.

## Out of Scope

Video, cloud relay, STUN/TURN вне LAN, запись аудио, ML/детектор плача, вторая сессия.

## Dependencies & Assumptions

- SIPSorcery принят владельцем (ADR-0005), версия >= 10.0.17; PackageReference добавляется в
  Infrastructure только при реализации media adapter (phase 2).
- IRealtimeTransport contract уже существует (001) и проверяется общим suite.
- Реальные устройства отсутствуют — end-to-end/Soak BLOCKED.