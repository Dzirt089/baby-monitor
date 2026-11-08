# Changelog

Все заметные изменения проекта описываются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версионирование — [SemVer](https://semver.org/lang/ru/).

## [Unreleased]

Device-прогон v0.3.1 (2026-11-08, Huawei nova Y90 + nova 13 Pro, Android 12) выполнен: pairing,
передача звука Child→Parent, **слышимость подтверждена владельцем**, MediaFlowing, reconnect Wi-Fi,
выключенный экран, чистый Stop — PASS; детали — docs/operations/device-test-results-2026-11-08.md.
Дальше — 007 (heartbeat/ParentLossAlarm/telemetry, calls/audio focus, BT/наушники, 8h soak,
100 циклов, battery без зарядки, TalkBack, camera↔camera, OEM-матрица; UX: индикация активной
фоновой сессии на экране выбора роли) и 008 (release readiness, owner-gated публикация/лицензия/
дистрибуция).

## [0.4.1] - 2026-11-08

### Fixed

- **Diagnostics telemetry «замирала»**: снимок с telemetry/uptime/health обновлялся только при смене
  состояния сеанса. Coordinator теперь публикует live-refresh snapshot каждую секунду во время run
  (State/Error не меняются); после Stop/Dispose таймер останавливается. Проверено на устройствах:
  counters/uptime растут без смены state.
- **Бесконечный Reconnecting при исчезнувшем peer**: повторные попытки discovery/connect ждали peer
  без таймаута, MaxAttempts не исчерпывался. Добавлен reconnectAttemptTimeout (default 20 s, только
  для retry-попыток): timeout → typed realtime.signaling_timeout → retry → после MaxAttempts
  terminal Failed. Первичная попытка не ограничена. На device: Parent уходит в Failed с typed ошибкой
  и alarm, а не висит в Reconnecting >60 s.
- **Parent alarm scoping**: terminal Failed Parent поднимает loss-тревогу только для loss-кодов
  (peer_unavailable/signaling_timeout/heartbeat_missed/media_flow_lost/media_path_absent/
  network_unavailable/tls_failure); локальные ошибки (unauthorized_peer/unsupported_version/audio.*/
  media.adapter_failure) больше не показывают «Связь потеряна!».

### Verified on devices (2026-11-08, Huawei nova Y90 + nova 13 Pro, Android 12)

- Heartbeat/media alarm при выключении Wi-Fi Child: Parent в течение ≤3 s показал «Связь потеряна!»,
  high-importance notification «Радионяня: связь потеряна» (отдельный канал), Media Reconnecting.
- Child после Wi-Fi off/on: typed Failed discovery.wifi_unavailable после reconnect-попыток;
  telemetry counters (↑11168, reconnect 5, uptime) видны в Diagnostics; crash buffer пуст.
- Повторная проверка после фиксов (live counters, terminal Failed + standalone alarm) — выполняется;
  результаты будут в docs/operations/device-test-results-2026-11-08.md. Звук/вибрация/DND alarm-канала
  и media-only alarm — device-only, 007.

## [0.4.0] - 2026-11-08

### Added

- **Control heartbeat** в signaling-сессии: периодический ping/pong по существующему TLS
  control-каналу (HeartbeatInterval 5 s, HeartbeatTimeout 15 s — единый порог из
  SipsorceryTransportOptions передаётся в Application/health; TimeProvider), событие
  `IRealtimeTransportSession.HeartbeatReceived`, typed `realtime.heartbeat_missed` при потере
  control-канала (независимо от media flow). Mixed-version совместим: v0.3.x уже отвечает pong
  на ping (protocol v1 не менялся), поэтому heartbeat работает с обновлённой стороной; для
  собственного alarm/health на старой стороне нужно обновление (рекомендуется обновить обе).
- **ParentLossAlarm в media-сеансе**: локальная тревога Parent при потере control heartbeat ИЛИ
  media flow (alarm-порог 15 s; media-loss фиксируется watchdog-ом), `MediaSessionSnapshot.ParentAlarmActive`,
  отдельное high-importance alarm-уведомление «Связь потеряна!» (звук/вибрация) и статус на экране
  мониторинга; тревога не зависит от ответа Child и видна также при терминальном Failed Parent.
- **Telemetry counters (частично)**: `MediaSessionTelemetry` в снимке сеанса (отправленные/принятые
  кадры, reconnect-попытки, uptime, последний media frame); строка метрик в экране «Диагностика»
  (без аудио/секретов). Lost/underruns/silence/buffer depth/encode-decode counters — 007 follow-up.
- SessionHealthMonitor учитывает heartbeat (Control Degraded/Lost) отдельно от media stall;
  heartbeat watchdog и first-frame media watchdog дают typed Degraded/Reconnecting.

### Known limitations

- Device-проверка v0.4.0 (heartbeat/pong на реальных устройствах, срабатывание тревоги, звук/
  вибрация канала, новые метрики) **ещё не выполнена**: после перезагрузки одного из тестовых
  телефонов пропал adb (нужно подтвердить USB-отладку/подключить кабель). v0.3.1 device pass
  (звук/owner confirmation) остаётся в силе; эти фичи — 007 phase 2.
- Автоматизированный 100-цикловый reconnect-тест на v0.3.1 оказался неустойчивым из-за поведения
  Wi-Fi драйвера Huawei (переассоциация ~25-28 s после toggle; после 8 циклов — typed Failed
  вследствие исчерпания reconnect-политики). Методика и повтор — 007 (не заявляется пройденным).
- На момент 0.4.0 ParentAlarmActive для терминального Failed Parent выставляется при любом терминальном
  отказе (включая локальные ошибки audio/adapter) — scoping только loss-кодов и текст тревоги — 007 follow-up.
- Пороги heartbeat/alarm — defaults (5/15 s, alarm 15 s; App composition передаёт 15 s в health);
  настройка/тюнинг по device-метрикам — 007. Смешанные версии: v0.3.x отвечает pong, но сама не
  поднимает heartbeat alarm.

## [0.3.1] - 2026-11-08

### Fixed

- UI мониторинга мог показать устаревший снимок «Мониторинг активен» после того, как media-сеанс
  уже завершился typed-ошибкой (гонка: результат StartAsync применялся поверх более нового Failed
  снимка). Теперь модель применяет актуальный host.Current и отображает код ошибки
  (например, media.no_trusted_peer) — обнаружено smoke-проверкой release APK v0.3.0 на эмуляторе.
- Регрессионный тест на гонку StartAsync/Failed в MonitoringScreenModel.

### Verified on devices

- 2026-11-08, Huawei nova Y90 (Child) + Huawei nova 13 Pro (Parent), Android 12: сопряжение
  офлайн QR (paste-fallback), одинаковый verification code, trusted store; MediaFlowing на обоих;
  Parent AudioTrack started (USAGE_MEDIA/CONTENT_TYPE_SPEECH); **владелец подтвердил слышимость**;
  reconnect Wi-Fi off/on → MediaFlowing; screen-off 12 с — передача продолжается; Stop снимает
  уведомления/AudioTrack. Battery/BT/calls/TalkBack/OEM — 007 (см. device results).

## [0.3.0] - 2026-11-08

**Кодовая база 005 phase 2: signaling/media/audio реализованы; на момент релиза слышимость ещё не
была подтверждена (подтверждена владельцем на v0.3.1, 2026-11-08).**

### Added

- Аутентифицированный signaling: TCP + TLS 1.2/1.3 с mutual authentication на эфемерных ECDSA
  P-256 сертификатах и app-layer identity assertion (ECDSA Keystore), pinning peer по SHA-256
  SubjectPublicKeyInfo из trusted store; версионированный протокол (hello/offer/answer/ping/pong/bye),
  лимиты сообщений и таймауты; cleartext/TLS-bypass запрещены (ADR-0007 Accepted, вариант A).
- Media: WebRTC audio-only (SIPSorcery 10.0.17), Opus 48 kHz mono 20 ms, DTLS-SRTP, ICE host-only,
  bounded drop-oldest очереди; Opus codec (Concentus) за портом IAudioCodec.
- Android audio: AudioRecord (Child capture) и AudioTrack (Parent playback, audio focus/route,
  focus loss/regain) за портами IAudioCapture/IAudioPlayback; connectivity monitor (Wi-Fi/Ethernet)
  для reconnect.
- Application: MonitoringMediaCoordinator (Child send / Parent receive, reconnect по ReconnectPolicy,
  media-stall watchdog, typed errors, immutable snapshots) + интеграция в foreground service и UI
  (состояние «Подключение…»/«Звук передаётся»/«Переподключение…»/ошибка в уведомлении и на экранах).
- Application composition root (Android Application) + MediaSessionFactory; trusted peer из trusted store.

### Security

- Signaling/media не используют cleartext; TLS validation не отключается, кастомный callback только
  для pinning в Signaling-адаптере; identity assertion связывает TLS-канал с доверенной identity.
- Аудиоданные и SDP не логируются и не сохраняются на диск (architecture tests); private keys —
  Android Keystore; аудио не записывается и не отправляется никуда, кроме peer.

### Known limitations (device-проверки ещё не выполнены)

- На момент 0.3.0 реальная слышимость/задержка/echo/audio focus/route и Android SslStream +
  SIPSorcery ICE/DTLS/Opus не были подтверждены; подтверждены на v0.3.1 (2026-11-08, Huawei nova Y90
  + nova 13 Pro) — см. [0.3.1] Verified on devices; остальные сценарии — 007.
- Автоматическое переподключение после установленной сессии реализовано кодом и unit/loopback
  tests; реальная потеря Wi-Fi/смена IP — device-прогон.
- Media path absent до первого аудиокадра детектируется watchdog-ом (typed media_flow_lost →
  Degraded/Reconnecting); реальная индикация/переподключение на устройствах — device-прогон.
- ParentLossAlarm в media-интеграцию пока не подключён; control heartbeat (ping) не отправляется
  (детект по transport state + media watchdog/first-frame timeout); telemetry counters (FR-022)
  не выделены — 007.
- OEM/Doze/8h soak/battery, TalkBack, Bluetooth/headset matrix — 007.
- LAN-availability: одиночный TCP connect+close или rogue mDNS-реклама могут временно помешать
  старту сеанса (typed Failed; trust/pinning не обходится); bounded retry/rate-limit — 007 (H5).
- Приложение не является медицинским изделием.

## [0.2.0] - 2026-11-08

**Тестовая сборка для проверки на реальных устройствах.** Приложение пока **не является рабочей
радионяней**: передача звука не реализована (005 phase 2), сетевой signaling и автоматическое
переподключение runtime-сессии отсутствуют.

### Added

- Экраны выбора роли и мониторинга с foreground service, постоянным уведомлением и действием
  «Остановить» (003/006 phase 1).
- Безопасное сопряжение двух устройств через офлайн QR: Child создаёт одноразовое приглашение и
  показывает QR → Parent сканирует и показывает QR ответа → Child сканирует ответ; обе стороны
  показывают одинаковый 6-значный verification code и подтверждают. Сканирование QR камерой
  (ZXing.Net, ADR-0011), доверенные устройства (просмотр/забыть/сопрячь заново), экран диагностики
  (004/006 phase 2).
- Обнаружение детского устройства в LAN через NSD/DNS-SD (адаптер; UI-интеграция — в 005) (002).
- Политики надёжности сессии: reconnect/backoff, health monitor, локальная тревога Parent
  (005 phase 1; runtime-интеграция — в 005 phase 2).

### Security

- Pairing-протокол ECDSA/ECDH P-256, private keys в Android Keystore (non-exportable), trusted store
  только с публичными identity/fingerprint/ролью/временем.
- QR приглашения (одноразовый token, TTL 5 минут) не логируется и не показывается текстом; кадры
  камеры не сохраняются и не отправляются; paste-поле маскируется и очищается после применения.
- `allowBackup=false`; cleartext/TLS-обходов нет.
- Исправлен interop ECDSA-подписей: Android Keystore возвращает DER, BCL ожидает IEEE P1363 —
  протокол принимает обе стандартные кодировки (regression tests).

### Fixed

- Android Keystore adapter: приведение private key через `JavaCast` (результат `GetKey` —
  `IKeyInvoker`) — реальная подпись больше не падает с `keystore_unavailable`.
- UI: дублирование инструкций на экране сопряжения, состояние ошибки после истечения приглашения,
  обработка отмены на экране диагностики, forget/re-pair без «тупика».

### Known limitations

- Передача звука/media, сетевой signaling и автоматическое переподключение runtime-сессии не
  реализованы: 005 phase 2.
- Реальный QR-обмен камерой между двумя устройствами, camera/OEM matrix, permission flows
  12+/13+/14+, TalkBack, 8-часовой soak — **не проверены**; эта сборка предназначена для
  первого device-прогона владельцем (см. docs/operations/device-test-checklist-v0.2.0.md).
- Офлайн-QR pairing создаёт **локальное** доверие: взаимное подтверждение peer появится только с
  аутентифицированным signaling в 005 phase 2; до этого сетевой проверки peer и повторных
  подключений нет.
- Приложение не является медицинским изделием и не заменяет наблюдение взрослого.

## [0.1.0] - 2026-10-20

Bootstrap-фаза репозитория: solution/каркас, docs/ADR, CI/CD, release-by-tag, contract test suite
для `IRealtimeTransport` (001 phase 1).