# Pairing Threat Model (004)

**Статус**: draft (004 phase 1) | **Дата**: 2026-11-07

## Assets

- Долгосрочные private identity keys (ECDSA P-256) — только в Android Keystore.
- Одноразовый pairing token (256-bit) — в QR Child, живёт <= 5 минут.
- Эфемерные ECDH private keys — только в памяти процесса.
- Общий shared secret / verification code / будущий session key.
- Trusted devices list (публичные identity + fingerprint + роль).

## Trust boundaries

- LAN считается недоверенной: любой может видеть mDNS/сетевой трафик, подменять сервисы,
  пытаться MITM.
- QR — out-of-band доверенный канал (физический экран), но сам по себе может быть сфотографирован.
- Android Keystore — доверенный hardware/OS boundary для private keys.
- Пользователь — доверенный арбитр verification code (сравнивает визуально).

## Threats и митигации

| Угроза | Митигация | Тест |
|---|---|---|
| MITM при обмене ключами | подписи Child/Parent + ECDH; verification code подтверждает совпадение транскрипта | happy path, tamper signature |
| Replay pairing-ответа | token 256-bit, single-use state, expiry 5 мин | replay/expiry tests |
| Перебор/подмена QR | версия протокола, подпись Child, fingerprint из QR | malformed/invalid signature/version |
| Утечка QR в лог/скриншот | QR не логируется; token одноразовый и короткоживущий | secret-scan tests |
| Кража/потеря устройства | ForgetPairedDevice удаляет trusted; повторный pairing обязателен | store removal test |
| Downgrade | strict protocol version, unsupported_version | version test |
| Несовпадение кода (атака/ошибка) | код 6 цифр; оба пользователя подтверждают; несовпадение/отказ → no trust | user_rejected/code mismatch |
| Компрометация процесса | out of scope для 004 (007/008 hardening, Keystore key non-exportable) | — |
| Keystore failure | typed error, no plaintext key fallback | adapter tests/architecture |

## Failure modes

- Invalid QR / unsupported version / expired / reused token → typed error, no session state change,
  secrets не логируются.
- Invalid signature / identity mismatch → reject, no trust saved.
- Code mismatch / user reject → no trust saved, invite invalidated.
- Trusted store failure → error, pairing не считается завершённым.
- Signaling в 005 обязан аутентифицироваться по paired identity; cleartext запрещён.

## Residual risks (BLOCKED)

- End-to-end на реальных устройствах (QR scan, Keystore OEM variations, screen capture/OS-level
  malware) не проверены: **BLOCKED: нет реальных устройств**.