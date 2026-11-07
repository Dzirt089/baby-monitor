# Changelog

Все заметные изменения проекта описываются в этом файле.
Формат основан на [Keep a Changelog](https://keepachangelog.com/ru/1.1.0/),
версионирование — [SemVer](https://semver.org/lang/ru/).

## [Unreleased]

### Added

- Экраны выбора роли и мониторинга с foreground service, постоянным уведомлением и действием
  «Остановить» (003/006 phase 1).
- Безопасное сопряжение двух устройств: офлайн-обмен QR (Child создаёт приглашение и показывает QR →
  Parent сканирует и показывает ответный QR → Child сканирует ответ), одинаковый 6-значный
  verification code, trusted devices (просмотр/забыть/сопрячь заново), сканирование QR камерой
  (ZXing.Net, ADR-0011), экран диагностики; ViewModels presentation-agnostic (004/006 phase 2).
- Обнаружение детского устройства в LAN через NSD/DNS-SD (002).
- Политики надёжности сессии: reconnect/backoff, health monitor, локальная тревога Parent
  (005 phase 1).

### Security

- Pairing-протокол ECDSA/ECDH P-256, private keys в Android Keystore, trusted store только с
  публичными данными; QR приглашения (token) не логируется, кадры камеры не сохраняются.
- Исправлен interop ECDSA-подписей: Android Keystore возвращает DER, BCL ожидает IEEE P1363 —
  протокол принимает обе стандартные кодировки (regression tests).

### Fixed

- Android Keystore adapter: приведение private key через JavaCast (IKeyInvoker) — подпись больше
  не падает с keystore_unavailable.

### Known limitations (BLOCKED)

- Передача аудио/media, сетевой signaling и автоматическое переподключение runtime-сессии не
  реализованы: 005 phase 2 **BLOCKED** (ADR-0007 Proposed, нет реальных устройств).
- Реальный QR-обмен камера↔камера, camera/OEM/TalkBack matrix, 8-часовой soak и профилирование —
  **BLOCKED: нет реальных устройств** (007).
- Приложение пока не выполняет функции радионяни и не является медицинским изделием.
