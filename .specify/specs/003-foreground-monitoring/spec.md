# Feature Specification: 003-foreground-monitoring

**Feature Branch**: 003-foreground-monitoring
**Created**: 2026-11-07
**Status**: In Progress (контракт + Android service реализованы; device-проверки BLOCKED)
**Input**: AGENTS.md разделы 9, 11, 20; roadmap 003

## Контекст

Мониторинг обязан работать через Android foreground service с постоянным уведомлением: роль,
состояние и действие «Остановить». Service запускается только явным действием пользователя при
видимой Activity, переживает уничтожение Activity и не хранит View/ViewModel/Activity ссылок.
Child service объявляет тип `microphone`, Parent — `mediaPlayback`. В 003 реальный аудио-pipeline ещё не
открывается (media — 005); сервис — фундамент для 005/006.

## User Scenarios

### US-1: Пользователь запускает мониторинг (P1)

Пользователь выбирает роль и подтверждает старт при видимом экране. При отсутствии разрешения на
уведомления старт отклоняется типизированной ошибкой, чтобы нельзя было потерять кнопку Stop.

### US-2: Мониторинг переживает уход из Activity (P1)

После запуска Activity может быть уничтожена — service продолжает работать, уведомление остаётся,
состояние доступно через host API.

### US-3: Остановка (P1)

Пользователь останавливает мониторинг из уведомления или из приложения: service останавливается,
уведомление убирается, снимок состояния становится Stopped.

## Functional Requirements

- **FR-001**: Порт `IForegroundSessionHost` + immutable `ForegroundSessionSnapshot`/состояния/ошибки в Application, без
  Android-типов.
- **FR-002**: Порт `INotificationPermission`: POST_NOTIFICATIONS (Android 13+) скрыт за permission adapter;
  отказ возвращает типизированную ошибку.
- **FR-003**: Android `MonitoringForegroundService`: StartForeground с ролью Child=@BT@microphone@BT@,
  Parent=@BT@mediaPlayback@BT@; постоянное уведомление с ролью, состоянием и действием Stop.
- **FR-004**: Start разрешён только при видимой Activity (IActivityVisibility); иначе
  `background_start_denied`. Идемпотентность: повторный старт той же роли — успех, другой роли — `already_running`.
- **FR-005**: Service не хранит Activity/View/ViewModel; уведомление открывает приложение через
  launch intent, Stop работает через PendingIntent без UI.
- **FR-006**: Остановка идемпотентна; отсутствие активной сессии — `not_running`; все ресурсы освобождаются.
- **FR-007**: Manifest: FOREGROUND_SERVICE, FOREGROUND_SERVICE_MICROPHONE,
  FOREGROUND_SERVICE_MEDIA_PLAYBACK, POST_NOTIFICATIONS, RECORD_AUDIO. RECORD_AUDIO нужен Android 14+
  для FGS-типа microphone (Child); запрашивается только для Child, Parent его не запрашивает и
  микрофон не открывает. AudioRecord открывается только в 005; service `exported=false`.
- **FR-008**: Android 13+ notification permission и Android 14+ foreground restrictions:
  старт из видимой Activity, FGS-тип соответствует роли, RECORD_AUDIO запрашивается до Child-старта,
  no background start; device-проверки на реальных прошивках остаются BLOCKED.

## Non-Functional Requirements

- **NFR-001**: Contract suite IForegroundSessionHost работает в CI без Android/service.
- **NFR-002**: Application не получает Android-типов; архитектурные тесты проверяют manifest и
  отсутствие microphone permissions.
- **NFR-003**: Эмуляторный smoke (install/launch; при возможности — adb-старт service) не выдаётся
  за device-проверку.

## Acceptance Criteria

- [x] AC-001: Порт/снимок/ошибки в Application; сборка без warnings.
- [x] AC-002: Fake + contract suite (start/stop/идемпотентность/события/ошибки) зелёный в CI.
- [x] AC-003: Android foreground service компилируется; merged manifest (service exported=false,
  foregroundServiceType=microphone|mediaPlayback) и permissions проверены architecture tests;
  emulator smoke: APK install, POST_NOTIFICATIONS grant, service объявлен в package. Runtime-старт
  service через adb **невозможен по дизайну** (non-exported); реальный старт/notification требуют UI
  (006) или instrumentation — **BLOCKED** без них.
- [x] AC-004: POST_NOTIFICATIONS adapter + typed denial; для Child дополнительно RECORD_AUDIO
  (Android 14+ FGS microphone), запрашивается только Child; без разрешений старт отклоняется
  типизированными ошибками.
- [-] AC-005: Activity может быть уничтожена при живом service — **BLOCKED**: нет устройств;
  emulator-старт service невозможен без UI/instrumentation (non-exported).
- [-] AC-006: Android 13+ notification permission и 14+ foreground restrictions на реальных
  прошивках — **BLOCKED: нет реальных устройств**.
- [-] AC-007: Battery/Doze/OEM поведение — **BLOCKED: нет реальных устройств** (007).

## Out of Scope

Реальный audio capture/playback и media pipeline (005), reconnect/health (005), pairing (004),
UI/ViewModel (006), wake locks, boot-start (запрещён), несколько сессий.

## Dependencies & Assumptions

- Android foreground service API, minSdk 24, targetSdk 36; POST_NOTIFICATIONS (33+).
- Реальные Android-устройства для AC-005/006/007 отсутствуют — BLOCKED.
- Activity/Composition wiring появится в 006; host адаптер готов к подключению.