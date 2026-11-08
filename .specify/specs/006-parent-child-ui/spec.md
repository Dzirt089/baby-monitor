# Feature Specification: 006-parent-child-ui

**Feature Branch**: 006-pairing-ui (phase 2; phase 1 merged в main PR #13)
**Created**: 2026-11-07
**Status**: Phase 2 реализована и device-verified (pairing UI/QR paste-fallback, verification code, trusted devices, diagnostics, ViewModels, a11y — на двух Huawei, 2026-11-08); media/audio — PASS (005 phase 2, v0.3.1, owner-confirmed звук); camera↔camera/13+/14+/OEM/TalkBack — 007
**Input**: AGENTS.md разделы 3, 5, 6, 9, 10, 13, 15; roadmap 006; ADR-0006 Accepted; ADR-0011 Accepted (ZXing.Net); 004 secure pairing protocol; 005 phase 2 Done (v0.3.1, device-verified)

## Контекст

Пользователю нужны явный выбор роли, запуск/остановка мониторинга, безопасное сопряжение двух
устройств, понятное состояние и диагностика. UI на .NET for Android (Activity + XML), MVVM:
Activity только связывает View и команды, VM (presentation-agnostic screen model) вызывает только
Application-порты и наблюдает immutable snapshots.

Передача звука и signaling реализованы в 005 phase 2 (ADR-0007 Accepted; v0.3.0/v0.3.1) и подтверждены на двух реальных Huawei 2026-11-08. Pairing (004-протокол)
выполняется **офлайн-обменом QR через физические экраны** (out-of-band канал, доверенный по
threat model 004): Child показывает QR приглашения → Parent сканирует и показывает QR ответа →
Child сканирует ответ → оба устройства показывают одинаковый 6-значный verification code →
пользователи подтверждают. Сеть при сопряжении не используется; это не заменяет
аутентифицированный signaling/media для повторных подключений (005), но позволяет завершить
pairing без него. UI честно показывает, что передача звука ещё не реализована.

## User Scenarios

### US-1: Выбор роли (P1, phase 1)

На старте пользователь выбирает «Ребёнок» или «Родитель»; роль явно отображается и определяет
разрешения/поведение (Child — microphone, Parent — playback; Parent не запрашивает микрофон).

### US-2: Запуск/остановка мониторинга (P1, phase 1)

Пользователь запускает мониторинг кнопкой; приложение запрашивает POST_NOTIFICATIONS и (для Child)
RECORD_AUDIO, стартует foreground service с постоянным уведомлением и кнопкой Stop; Stop в
приложении и в уведомлении останавливает сервис. Ошибки показываются typed-кодом и сообщением.

### US-3: Состояние и диагностика (P1, phase 1)

Экран показывает роль, состояние foreground-сессии (Starting/Running/Stopping/Stopped/Failed),
доступность сервиса; при Failed — безопасное сообщение без секретов.

### US-4: Child создаёт приглашение и показывает QR (P1, phase 2)

Child открывает экран сопряжения, по явному действию создаёт одноразовое приглашение (004-протокол,
token живёт <= 5 минут), видит QR и оставшееся время. По истечении времени приглашение помечается
истёкшим и может быть создано новое. Token/QR не показываются текстом и не логируются.

### US-5: Parent сканирует QR приглашения и показывает ответ (P1, phase 2)

Parent сканирует QR камерой (или вставляет payload в диагностическом fallback-режиме), приложение
проверяет версию/срок/подпись Child, показывает 6-значный verification code и QR-ответ для Child.
Ответный QR содержит только публичные данные (identity Parent, эфемерный ключ, подпись).

### US-6: Пользователи подтверждают code и завершают сопряжение (P1, phase 2)

Child сканирует QR-ответ Parent; оба устройства независимо выводят одинаковый 6-значный code.
Пользователи визуально сравнивают код и подтверждают. PairedDevice сохраняется в trusted store
после подтверждения на этом устройстве; офлайн QR-обмен не даёт канала подтверждения peer, поэтому
доверие локальное и односторонне возможное (residual risk, взаимность проверяется на
signaling-этапе 005); при отказе/несовпадении доверие не сохраняется. Повторный запуск показывает уже сопряжённое устройство и позволяет перейти к
мониторингу, забыть устройство или сопрячь заново.

### US-7: Диагностика и управление доверием (P2, phase 2)

Отдельный экран диагностики показывает роль, состояние foreground-сессии, последнюю безопасную
ошибку, список доверенных устройств (роль peer, короткий fingerprint, время сопряжения), версию
приложения и честную пометку, что media не реализована. Доверенное устройство можно забыть.

## Functional Requirements

- **FR-001**: Экран выбора роли (Child/Parent), без скрытого default; Unknown не выбирается.
- **FR-002**: MonitoringActivity получает роль через Intent, стартует/останавливает
  IForegroundSessionHost, показывает состояние и typed-ошибки; никакой бизнес-логики в Activity.
- **FR-003**: Permission bridge: Activity реализует IPermissionRequestHost и IActivityVisibility;
  POST_NOTIFICATIONS/RECORD_AUDIO запрашиваются только по явному действию; Parent не запрашивает mic.
- **FR-004**: Composition root — в App/Composition (per-Activity scope), без global service locator;
  Activity не создаёт Android-сервисы/адаптеры напрямую (кроме связки View↔команды).
- **FR-005**: UI показывает только реальное состояние; не заявляет работающую передачу звука, пока
  media реализована в 005 phase 2 (v0.3.1); device-подтверждение — 2026-11-08.
- **FR-006**: Состояние foreground-сессии переживает пересоздание Activity (сервис независим;
  экран перечитывает snapshot).
- **FR-007**: Strings — в resources (русский), a11y (contentDescription для интерактивных
  элементов), без секретов.
- **FR-008**: Pairing доступен из выбора роли до мониторинга; экран мониторинга не содержит
  ложных pairing-кнопок; после phase 2 — только реальные действия (диагностика/сопряжение).
- **FR-009**: Диагностика: роль, состояние foreground-сессии, последняя ошибка, доверенные
  устройства (роль/fingerprint short/время), версия приложения; без аудио/секретов/personal data.
- **FR-010**: Architecture tests: App зависит от Application/Platform только через порты; ViewModel
  не использует Android/Service напрямую; Domain/Application без Android/ZXing.
- **FR-011**: Вход в pairing: после выбора роли открывается PairingActivity; если для локальной роли
  уже есть доверенное устройство — показывается статус «сопряжено» с действиями «к мониторингу»,
  «забыть», «сопрячь заново». Без доверия доступен честный переход к мониторингу (media всё равно
  BLOCKED) с пометкой, что peer не сопряжён.
- **FR-012**: Child pairing: ChildPairingScreenModel создаёт приглашение через
  ChildPairingSession (Android Keystore identity, CSPRNG token), отдаёт QR payload, ExpiresAt и
  оставшееся время; по истечении — typed expired и возможность создать новое приглашение.
- **FR-013**: Parent pairing: ParentPairingScreenModel принимает отсканированный/вставленный QR,
  проверяет протокол через ParentPairingSession (версия/срок/подпись), отдаёт verification code и
  QR-ответ (PairingResponseQrCodec, только публичные данные).
- **FR-014**: Child завершает pairing: сканирует QR-ответ, получает PairingResponse через
  PairingResponseQrCodec, выводит verification code через ChildPairingSession; при совпадении кода и
  подтверждении пользователем сохраняет PairedDevice в trusted store. Доверие не сохраняется при
  отказе/несовпадении/ошибке store.
- **FR-015**: Камера: только на экране pairing и только по явному действию пользователя; runtime
  permission CAMERA запрашивается через permission bridge; кадры не сохраняются и не логируются;
  сканер останавливается после распознавания, при паузе Activity и при уничтожении; отсутствие
  камеры/отказ разрешения — typed ошибка без падения.
- **FR-016**: QR payload приглашения — секрет (одноразовый token): не логируется, не отображается
  текстом, не попадает в ошибки/уведомления; QR-ответ содержит только публичные данные. Ни один из
  QR/verification code не пишется в логи, analytics или crash-сообщения.
- **FR-017**: Диагностический fallback «вставьте QR-текст»: тот же путь валидации, что и камера;
  поле маскируется, очищается после применения; доверие всё равно требует verification code и
  подтверждения; fallback нужен для emulator/no-camera проверок и не ослабляет протокол.
- **FR-018**: ViewModels выделены в отдельные presentation-agnostic классы Application
  (ChildPairingScreenModel, ParentPairingScreenModel, TrustedDevicesScreenModel,
  DiagnosticsScreenModel) и покрыты unit-тестами с fakes; Activity не содержит бизнес-логики.
- **FR-019**: a11y/локализация: contentDescription у QR-областей и кнопок, размеры интерактивных
  элементов >= 48dp (кнопки 56dp), текст читаем TalkBack, все пользовательские строки — resources
  (ru), без hardcoded текста в коде.
- **FR-020**: Architecture/package policy: ZXing.Net 0.16.11 — только Platform.Android (ADR-0011,
  THIRD-PARTY-NOTICES); CAMERA объявлен в manifest, camera hardware — uses-feature required=false;
  Application/Domain не ссылаются на Android/ZXing; тесты фиксируют это.
- **FR-021**: Честность состояний: pairing работает без сети, но UI не заявляет сетевую связь,
  повторное подключение или передачу аудио; media/audio подтверждены на 2 устройствах (v0.3.1), расширенный device E2E — 007.

## Non-Functional Requirements

- **NFR-001**: Pairing UI-flow покрыт unit-тестами (child↔parent roundtrip, expiry, invalid QR,
  tamper signature, reject, store failure, dispose) на Application-уровне с fakes; Android camera /
  два реальных устройства — выполнено 2026-11-08 (pairing paste-fallback; camera↔camera — 007).
- **NFR-002**: Domain/Application без Android-типов и без ZXing; Keystore/camera — только
  Platform.Android; App — только Activity/XML + связка с screen models.
- **NFR-003**: Camera2 + ImageReader на background-потоке; кадры не сохраняются; bounded обработка
  (drop frames), без утечек camera/ImageReader при Stop/Dispose/OnPause.
- **NFR-004**: Emulator smoke + реальные Huawei (v0.3.1): pairing paste-fallback, UI/навигация/
  диагностика, crash buffer пуст; camera↔camera на реальных устройствах — 007.

## Acceptance Criteria

- [x] AC-001: Role selection + MonitoringActivity компилируются; start/stop foreground service
  работают на эмуляторе (runtime smoke через permission grant).
- [x] AC-002: Parent не запрашивает RECORD_AUDIO; Child запрашивает только по действию Start.
- [x] AC-003: Typed-ошибки показываются безопасно; состояние читается после recreate (snapshot).
- [x] AC-004: Emulator UI smoke: выбор роли → Start → service Running/notification → Stop → Stopped.
- [x] AC-005: Pairing UI/QR: Child invite QR → Parent scan/paste → Parent response QR → Child
  scan/paste → одинаковый code → оба confirm → PairedDevice в trusted store; покрыто Application
  unit-тестами полного офлайн-цикла и emulator smoke (QR render + host verify + camera open);
  реальный обмен между двумя реальными Huawei выполнен 2026-11-08 (QR paste-fallback: code 848016,
  trusted store обоих; camera↔camera — 007).
- [x] AC-006: Diagnostics screen + TrustedDevices (просмотр/forget/re-pair) + ViewModels в отдельных
  классах + a11y/локализация; проверено build/tests/emulator smoke.
- [-] AC-007: Android 12 permission flows/FGS/pairing на реальных Huawei — PASS; 13+/14+ OEM/
  camera↔camera/TalkBack — **007**.
- [x] AC-008: Реальная передача звука/media — PASS: v0.3.1, MediaFlowing на обоих, владелец
  подтвердил слышимость (2026-11-08); camera↔camera и расширенная матрица — 007.

## Out of Scope

Видео/облако/двусторонняя связь (вне MVP), camera↔camera на реальных устройствах (007), QR scanning на
реальных камерах — 007; AndroidX/CameraX и Google Play Services (камера — platform
Camera2), изменение 004-криптопротокола, release signing/публикация.

## Dependencies & Assumptions

- 004-протокол (ChildPairingSession/ParentPairingSession/PairingQrCodec/PairingResponseQrCodec)
  реализован и покрыт тестами; private keys — Android Keystore.
- ADR-0011 Accepted: ZXing.Net 0.16.11 (Apache-2.0) в Platform.Android для QR encode/decode.
- Camera2 — platform API (API 24+), без AndroidX-пакетов.
- Camera↔camera/2-device camera E2E не выполнялся (использован paste-fallback) — 007; реальные устройства есть.
- Офлайн QR-обмен не заменяет signaling 005: runtime-сессия по-прежнему требует
  аутентифицированного канала; повторные подключения — 005.