# Feature Specification: 006-parent-child-ui

**Feature Branch**: 006-parent-child-ui
**Created**: 2026-11-07
**Status**: In Progress (phase 1: role selection + monitoring/start/stop + permissions; phase 2: pairing UI/QR and diagnostics)
**Input**: AGENTS.md разделы 3, 5, 9, 13; roadmap 006; ADR-0006 Accepted (.NET for Android)

## Контекст

Пользователю нужны явный выбор роли, запуск/остановка мониторинга, понятное состояние и диагностика.
UI на .NET for Android (Activity + XML), MVVM: Activity только связывает View и команды, VM вызывает
только Application-порты и наблюдает immutable snapshots. Полноценная передача звука и pairing
канала по сети ещё не реализованы (005 phase 2 BLOCKED: ADR-0007 Proposed + нет устройств), поэтому
UI честно показывает состояние foreground-сессии и не утверждает, что звук уже передаётся.

## User Scenarios

### US-1: Выбор роли (P1)

На старте пользователь выбирает «Ребёнок» или «Родитель»; роль явно отображается и определяет
разрешения/поведение (Child — microphone, Parent — playback; Parent не запрашивает микрофон).

### US-2: Запуск/остановка мониторинга (P1)

Пользователь запускает мониторинг кнопкой; приложение запрашивает POST_NOTIFICATIONS и (для Child)
RECORD_AUDIO, стартует foreground service с постоянным уведомлением и кнопкой Stop; Stop в
приложении и в уведомлении останавливает сервис. Ошибки показываются typed-кодом и сообщением.

### US-3: Состояние и диагностика (P1)

Экран показывает роль, состояние foreground-сессии (Starting/Running/Stopping/Stopped/Failed),
session id, доступность сервиса; при Failed — безопасное сообщение без секретов.

## Functional Requirements

- **FR-001**: Экран выбора роли (Child/Parent), без скрытого default; Unknown не выбирается.
- **FR-002**: MonitoringActivity получает роль через Intent, стартует/останавливает
  IForegroundSessionHost, показывает состояние и typed-ошибки; никакой бизнес-логики в Activity.
- **FR-003**: Permission bridge: Activity реализует IPermissionRequestHost и IActivityVisibility;
  POST_NOTIFICATIONS/RECORD_AUDIO запрашиваются только по явному действию; Parent не запрашивает mic.
- **FR-004**: Composition root — в App/Composition (per-Activity scope), без global service locator;
  Activity не создаёт Android-сервисы напрямую.
- **FR-005**: UI показывает только реальное состояние; не заявляет работающую передачу звука, пока
  media не реализована (005 phase 2 BLOCKED).
- **FR-006**: Состояние foreground-сессии переживает пересоздание Activity (сервис независим;
  экран перечитывает snapshot).
- **FR-007**: Strings — в resources (русский), a11y (contentDescription для кнопок), без секретов.
- **FR-008**: Pairing UI (QR/код) — phase 2; до этого экран мониторинга без ложных pairing-кнопок.
- **FR-009**: Диагностика: session id/state/last error; без аудио/секретов/personal data.
- **FR-010**: Architecture tests: App зависит от Application/Platform только через порты; ViewModel
  не использует Android Service напрямую (phase 2 по мере появления VM).

## Acceptance Criteria

- [ ] AC-001: Role selection + MonitoringActivity компилируются; start/stop foreground service
  работают на эмуляторе (runtime smoke через permission grant).
- [ ] AC-002: Parent не запрашивает RECORD_AUDIO; Child запрашивает только по действию Start.
- [ ] AC-003: Typed-ошибки показываются безопасно; состояние читается после recreate.
- [ ] AC-004: Emulator UI smoke: выбор роли → Start → service Running/notification → Stop → Stopped.
- [ ] AC-005: Pairing UI/QR — phase 2.
- [-] AC-006: Проверка на реальных устройствах (12+/13+/14+ permission flows, OEM) — **BLOCKED**.
- [-] AC-007: Проверка a11y на реальных TalkBack/OEM — **BLOCKED**.

## Out of Scope

Реальный audio media (005 phase 2 BLOCKED), QR rendering/scanning (phase 2), cloud, video.