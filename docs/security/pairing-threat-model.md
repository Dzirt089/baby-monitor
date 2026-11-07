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

- Потеря Keystore identity (reinstall/clear data, OEM keystore failure) → повторное сопряжение;
  auto-recreate не выполняется.
- Trusted store не восстанавливается через backup (llowBackup=false); integrity/rollback binding — 005.
- Ротация/миграция identity alias — 007/008.

- End-to-end на реальных устройствах (QR scan, Keystore OEM variations, screen capture/OS-level
  malware) не проверены: **BLOCKED: нет реальных устройств**.

## 006 phase 2: офлайн-обмен QR через экраны

Сетевой signaling-канал (005 phase 2) BLOCKED, поэтому первичное сопряжение по 004-протоколу
завершается out-of-band: Child показывает QR приглашения, Parent сканирует его и показывает QR
ответа, Child сканирует ответ. Оба устройства независимо выводят verification code, пользователи
сравнивают и подтверждают.

### Активы и границы

- QR приглашения содержит одноразовый token (секрет), TTL 5 минут. QR ответа — только публичные
  данные (identity Parent, эфемерный ECDH-ключ, подпись).
- Камера — локальный датчик; кадры обрабатываются только в памяти и не сохраняются.
- Paste-fallback (вставка текста QR) — диагностический путь для устройств без камеры/эмулятора;
  доверие всё равно требует verification code и подтверждения.

### Угрозы и митигации (phase 2)

| Угроза | Митигация | Тест |
|---|---|---|
| Подмена QR-ответа (MITM/скриншот) | подпись Parent, привязка response transcript к hash приглашения; чужой/повторный ответ отклоняется | unit: подмена подписи, ответ другого Parent |
| Replay приглашения/ответа | token one-time + TTL 5 мин; Child-сессия допускает один ответ; повторный QR не даёт нового доверия | unit: повторное использование, expiry |
| Подбор verification code | code выводится из ECDH shared secret + транскрипта, не из QR; сравнение пользователями обязательно | unit: code mismatch/user reject |
| Секреты в логах/UI | QR payload не логируется и не показывается текстом; paste-поле маскируется и очищается; code не логируется | architecture: secret-in-log test; review strings |
| Камера/кадры | кадры не пишутся на диск, не отправляются, не логируются; сканер останавливается после распознавания/OnPause/Dispose | emulator smoke; device matrix BLOCKED |
| Скриншот QR invitation | token одноразовый и короткоживущий; подтверждение требует физического доступа к обоим устройствам | residual risk (BLOCKED: device) |
| Забытое устройство | trusted store: просмотр и forget; после forget повторное подключение требует нового pairing | unit/integration trusted store |
| Подмена trusted store (локальный malware) | out of scope 006; integrity/rollback binding — 005/008 | residual |

### Failure modes

- Камера недоступна/отказ разрешения → typed error + paste-fallback, без падения.
- Отсканирован не тот QR (ответ вместо приглашения и наоборот) → invalid_qr, состояние не меняется.
- Истёкший/повторный QR → expired/token_reused, доверие не сохраняется.
- Несовпадение/отказ code → user_rejected/code_mismatch, no trust.
- Ошибка trusted store → pairing не считается завершённым; retry возможен.
- Сеть при сопряжении не используется — LAN MITM к pairing не применим; signaling для runtime-
  сессии по-прежнему обязан быть аутентифицирован (005, ADR-0007).

### Residual risks (offline QR flow, 006 phase 2)

- **Одностороннее доверие**: офлайн QR-обмен не даёт канала, по которому Child узнаёт, что Parent
  подтвердил код, и наоборот. Каждая сторона сохраняет PairedDevice локально после собственного
  подтверждения; возможен случай, когда доверие сохранено только на одной стороне (Parent подтвердил
  раньше, чем Child отсканировал ответ, или Child подтвердил при отказе Parent). Для runtime-сессий
  (005) обе стороны обязаны проверить доверие peer при signaling; взаимность подтверждения —
  требование signaling-этапа, а не offline QR. До реализации 005 runtime-подключения BLOCKED.
- **Скриншот QR приглашения** — token одноразовый и короткоживущий (<= 5 минут), ответный QR содержит
  только публичные данные; компрометация экрана/OS-level malware — вне scope 006 (007/008).
- **Camera preview/orientation на OEM** — preview может быть повёрнут/растянут без sensor-orientation
  transform; QR-декодирование от этого не зависит (кадры идут из ImageReader), но UX требует проверки
  на реальных устройствах: **BLOCKED** (007).

### Residual risks (BLOCKED)

- Реальный QR-обмен камера↔камера на двух устройствах, различные OEM/камеры, яркость/размер экрана
  и TalkBack не проверены: **BLOCKED: нет реальных устройств**.
- Скриншоты/OS-level malware и компрометация процесса — вне scope 006 (007/008 hardening).