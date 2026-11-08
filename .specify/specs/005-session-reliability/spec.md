# Feature Specification: 005-session-reliability

**Feature Branch**: 005-phase2-media
**Created**: 2026-11-07 (обновлено 2026-11-08)
**Status**: Phase 1 merged (PR #12). Phase 2 **unblocked**: ADR-0007 Accepted (2026-11-08, вариант A),
SIPSorcery разрешён (ADR-0005, >= 10.0.17), у владельца есть 2 физических Android-устройства для
device-проверки. До подтверждения слышимости владельцем приложение не объявляется работающей
радионяней.
**Input**: AGENTS.md разделы 2, 6, 7, 9, 10, 11, 15; roadmap 005; ADR-0005 (SIPSorcery),
ADR-0007 Accepted (signaling/TLS/pinning, DTLS-SRTP), ADR-0011 (QR pairing UI, 006)

## Контекст

Надёжность сеанса важнее минимальной задержки: после кратковременного разрыва приложение обязано
автоматически переподключиться, а Parent — локально поднять тревогу при потере control channel или
media flow, не полагаясь на ответ Child. «Transport connected» не равно «пользователь слышит звук»:
control и media проверяются независимо.

Phase 1 (merged) реализовала доменный КА, политику backoff, health-монитор и локальную тревогу.
Phase 2 реализует аутентифицированный signaling, защищённый media (WebRTC/Opus/DTLS-SRTP) и
Android audio adapters (AudioRecord/AudioTrack), затем — device-проверки владельцем на двух
устройствах. Первичное сопряжение уже есть (004/006) и переиспользуется: trusted store даёт
identity peer для pinning TLS, NSD (002) — endpoint детского устройства.

## User Scenarios

### US-1: Автоматическое переподключение (P1, phase 1 + phase 2)

После потери heartbeat/media или transport error сеанс переходит Degraded → Reconnecting,
backoff-задержки растут с jitter и ограничены сверху; при исчерпании попыток — Failed с typed
причиной. Ручной Stop немедленно прекращает reconnect loop (Stopping → Idle).

### US-2: Parent узнаёт о потере связи (P1, phase 1 + phase 2)

Parent поднимает локальную тревогу, если peer не подаёт признаков жизни дольше порога. Тревога
не зависит от Child: её вычисляет Parent по локальному времени/таймеру, и она снимается после
восстановления.

### US-3: Различимые состояния здоровья (P1, phase 1)

Приложение различает потерю heartbeat, остановку media flow, transport error и ручную остановку;
SessionHealth показывает control/media/capture/playback по отдельности.

### US-4: Parent подключается к ребёнку после сопряжения (P1, phase 2)

Child публикует NSD-сервис и слушает TLS signaling. Parent находит доверенное детское устройство
через discovery, устанавливает TLS-соединение с pinning identity из trusted store, обменивается
SDP и поднимает WebRTC-сессию. Недоверенный/чужой peer отклоняется до обмена media-параметрами.
Пользователь видит состояние Connecting → Connected; при ошибке — typed причина (без секретов).

### US-5: Реальная односторонняя передача звука Child → Parent (P1, phase 2)

Child захватывает микрофон (AudioRecord), кодирует Opus и отправляет в DTLS-SRTP media; Parent
принимает, декодирует и воспроизводит (AudioTrack) с audio focus/route. Child не создаёт playback,
Parent не запрашивает RECORD_AUDIO. При перегрузке старые кадры отбрасываются, latency не растёт
безгранично; аудио не пишется на диск и не логируется.

### US-6: Устойчивость реального сеанса (P1, phase 2)

При кратковременной потере Wi-Fi/смене IP сеанс переходит в Reconnecting, discovery/resolve
выполняется заново, TLS/WebRTC пересоздаются; при восстановлении Parent снова слышит звук, тревога
снимается. При выключенном экране/background service сеанс продолжает работать.

## Functional Requirements

### Phase 1 (merged)

- **FR-001** … **FR-007**: MonitoringSession КА, DisconnectReason, SessionHealth, ReconnectPolicy,
  SessionHealthMonitor, ParentLossAlarm, ручной stop/reconnect — реализованы и покрыты тестами.

### Phase 2 (signaling/media/audio)

- **FR-011**: Signaling — TCP в LAN, защищённый TLS 1.2+ с mutual authentication. Сертификат/identity
  peer принимается только при совпадении SHA-256 SubjectPublicKeyInfo с fingerprint доверенного
  устройства из trusted store (pinning). Недоверенный peer отклоняется до SDP/media.
  Cleartext запрещён; глобальное отключение TLS validation и DangerousAcceptAnyServerCertificateValidator
  запрещены.
- **FR-012**: Если .NET SslStream на Android не может использовать non-exportable Android Keystore
  private key, допускается fallback: TLS с эфемерным сертификатом для шифрования + app-layer ECDSA
  identity assertion, подписанный Keystore identity над nonce/транскриптом, с тем же pinning.
  Ослабление аутентификации peer не допускается; выбор и тесты фиксируются.
- **FR-013**: Child запускает signaling listener и публикует NSD-сервис (порт); Parent выполняет
  discover → resolve → TLS connect. Порт/endpoint не считается identity.
- **FR-014**: Signaling protocol версионирован: hello (protocol version, session id/nonce, identity
  assertion), SDP exchange, control messages. Неизвестная версия → unsupported_version без
  установления media. Replay/повторное соединение не даёт нового доверия.
- **FR-015**: Media — SIPSorcery за IRealtimeTransport (Infrastructure): WebRTC audio-only, Opus,
  DTLS-SRTP; STUN/TURN вне scope; ICE — host candidates в LAN. Адаптер проходит общий contract suite.
- **FR-016**: Android audio adapters за Application-портами: Child — только AudioRecord (capture);
  Parent — только AudioTrack (playback) + audio focus/route. Parent не запрашивает RECORD_AUDIO;
  Child не создаёт playback. Отдельные typed ошибки capture/playback/underrun.
- **FR-017**: Аудио-pipeline: bounded buffers между стадиями, drop-oldest при перегрузке, кадры
  ~20 мс, без выделения больших массивов на каждый кадр; Opus-encode/ decode; jitter buffer — в
  media-стеке; никакой записи PCM/Opus на диск и в логи.
- **FR-018**: Control heartbeat и media health проверяются независимо; transport Connected не
  означает «слышно». SessionHealth отражает control/media/capture/playback; ParentLossAlarm
  срабатывает по локальному порогу.
- **FR-019**: Reconnect: одна политика (ReconnectPolicy phase 1), новые попытки пересоздают
  discovery/TLS/WebRTC; network callback отменяет устаревшие операции; ручной Stop отменяет loop.
- **FR-020**: Lifecycle: сеанс живёт в foreground service (003), Activity может быть уничтожена;
  Stop освобождает socket/TLS/WebRTC/audio channels; после Dispose нет фоновых задач.
- **FR-021**: Ошибки типизированы и безопасны: нет недоверенного peer, TLS/pinning failure,
  signaling timeout, media path absent, media stalled, capture unavailable, playback unavailable,
  network lost, protocol version mismatch. Пользовательское сообщение отделено от технической
  причины; секреты/SDP/приватные IP не логируются.
- **FR-022**: Телеметрия — counters/aggregates (packets sent/received/lost, RTT, jitter, media
  silence, buffer depth, encode/decode duration, reconnect count/duration), без аудиоданных и
  секретов; горячие audio callbacks не логируются на каждый кадр.
- **FR-023**: Device-проверки: владелец проходит чек-лист на 2 устройствах (слышимость,
  выключенный экран, потеря Wi-Fi/переподключение, входящий звонок/focus, изменение маршрута,
  battery/time). Результаты фиксируются в 005/007; до подтверждения звук не объявляется
  работающим.

## Non-Functional Requirements

- **NFR-001**: Тесты детерминированы (fake TimeProvider, fake transport/audio adapters, no sleep в
  unit tests); contract suites для IRealtimeTransport и audio ports.
- **NFR-002**: Application/Domain без Android/SIPSorcery/WebRTC-типов; SIPSorcery только в
  Infrastructure; Android audio/TLS/platform — только Platform.Android/Infrastructure.
- **NFR-003**: Security-sensitive review обязателен для TLS/pinning/signaling/media изменений;
  architecture tests: отсутствие cleartext/отключения TLS validation, SIPSorcery не в Application,
  аудио-secret-log checks.
- **NFR-004**: Device-тесты на 2 устройствах владельца (по чек-листу); расширенная OEM-матрица,
  8h soak, battery — 007; до прогона честный статус «не проверено».

## Acceptance Criteria

- [x] AC-001: Domain КА + переходы/инварианты покрыты unit-тестами (phase 1).
- [x] AC-002: ReconnectPolicy (рост, cap, jitter, детерминизм) покрыт тестами (phase 1).
- [x] AC-003: Health monitor и ParentLossAlarm покрыты тестами с fake TimeProvider (phase 1).
- [x] AC-004 (реализовано+тесты; Android SslStream путь — device): Signaling TLS + pinning + identity assertion реализованы, покрыты contract/security
  тестами (недоверенный peer/replay/version/downgrade); без cleartext и отключения TLS validation.
- [x] AC-005 (реализовано+тесты; реальный Android media/audio — device): Media adapter SIPSorcery (Opus/DTLS-SRTP) за IRealtimeTransport проходит общий
  contract suite; audio adapters (AudioRecord/AudioTrack) реализуют Application-порты и role
  invariants (Child send-only, Parent receive-only).
- [x] AC-006: End-to-end сессия «Parent подключается к Child и слышит звук» **подтверждена владельцем**
  на двух реальных устройствах (Huawei nova Y90 + nova 13 Pro, Android 12, v0.3.1, 2026-11-08;
  pairing code 848016, MediaFlowing, Parent AudioTrack started). Детали:
  docs/operations/device-test-results-2026-11-08.md
- [-] AC-007: Wi-Fi loss/reconnect PASS на 2 устройствах (off/on 5 с → Connecting → MediaFlowing),
  screen-off 12 с PASS; 100 циклов, 8h soak, звонки/audio focus, смена IP, OEM/Doze — **BLOCKED/007**
  (нет полной матрицы/части сценариев).
- [-] AC-008: Battery drain/CPU/temperature/allocations — PARTIAL: ~8 мин на USB-питании
  (76 %/31 °C; 70 %/35 °C), валидный drain без зарядки — 007.
- [ ] AC-009 (follow-up 007): control heartbeat (ping/peers) и ParentLossAlarm интеграция в media-сеанс;
  media path absent до первого аудиокадра уже детектируется watchdog-ом (Degraded/Reconnecting).

## Out of Scope

Video, cloud relay, STUN/TURN вне LAN, запись/хранение аудио, ML/детектор плача, двусторонняя
связь, несколько одновременных сессий, Google Play/updater (ADR-0008), публичный релиз.

## Dependencies & Assumptions

- ADR-0007 Accepted (2026-11-08, вариант A): mutual TLS + pinning self-signed identity, media
  SIPSorcery DTLS-SRTP; threat model обновлён.
- SIPSorcery PackageReference >= 10.0.17 разрешён владельцем (ADR-0005); дополнительные пакеты
  (Opus/Concentus/SIPSorcery.Media) — только после ADR/лицензии/vuln scan, если нужны.
- Pairing/trusted store (004/006), NSD discovery (002), FGS (003) — готовы и переиспользуются.
- Два Android-устройства владельца доступны для ручных device-тестов; adb-автоматизация зависит от
  подключения устройств к ПК (уточняется владельцем).