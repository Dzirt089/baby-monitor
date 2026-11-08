# Changelog

Все заметные изменения проекта описываются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версионирование — [SemVer](https://semver.org/lang/ru/).

## [Unreleased]

Следующая работа: 007-device-hardening по результатам device-прогонов 0.3.0 (OEM/Doze/audio
route/TalkBack, 8h soak) и 008-release-readiness; device-подтверждение слышимости владельцем — до
объявления приложения рабочей радионяней.

## [0.3.0] - 2026-11-08

**Кодовая база 005 phase 2: signaling/media/audio реализованы, но слышимость ещё НЕ подтверждена
владельцем на двух устройствах** — приложение пока не объявляется рабочей радионяней.

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

- Реальная слышимость, задержка, echo/audio focus/route, работа Android SslStream + SIPSorcery
  ICE/DTLS/Opus на устройствах **не подтверждены** — нужен прогон владельцем на Huawei nova 13 Pro
  и Huawei nova Y90 (v0.3.0 checklist).
- Автоматическое переподключение после установленной сессии реализовано кодом и unit/loopback
  tests; реальная потеря Wi-Fi/смена IP — device-прогон.
- Media path absent до первого аудиокадра детектируется watchdog-ом (typed media_flow_lost →
  Degraded/Reconnecting); реальная индикация/переподключение на устройствах — device-прогон.
- ParentLossAlarm в media-интеграцию пока не подключён; control heartbeat (ping) не отправляется
  (детект по transport state + media watchdog/first-frame timeout); telemetry counters (FR-022)
  не выделены — 007.
- OEM/Doze/8h soak/battery, TalkBack, Bluetooth/headset matrix — 007.
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