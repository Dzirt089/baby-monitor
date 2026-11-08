# Pairing Threat Model (004)

**Статус**: living document (004 + 006 phase 2 + 005 phase 2 signaling/media) | **Дата**: 2026-11-08

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

- End-to-end pairing на двух реальных Huawei — PASS 2026-11-08 (paste-fallback, code совпал,
  trusted store); не проверены screen capture/OS-level malware, camera↔camera и Keystore OEM
  variations — 007.

## 006 phase 2: офлайн-обмен QR через экраны

Сетевой signaling реализован в 005 phase 2 (v0.3.1, ADR-0007 Accepted), но первичное сопряжение по
004-протоколу сознательно остаётся out-of-band (продукт-решение: меньше поверхности атаки до доверия): Child показывает QR приглашения, Parent сканирует его и показывает QR
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

- Реальный QR-обмен камера↔камера не выполнялся (NOT RUN; использован paste-fallback PASS 2026-11-08);
  различные OEM/камеры, яркость/размер экрана и TalkBack — 007.
- Скриншоты/OS-level malware и компрометация процесса — вне scope 006 (007/008 hardening).

## 005 phase 2: signaling и media (ADR-0007 Accepted, вариант A)

Сетевой signaling и передача звука включаются после первичного сопряжения. Локальная LAN
недоверенная; доверие к peer определяется только сохранённой identity из trusted store (004/006),
а не IP/mDNS-именем.

### Схема (кратко)

- Child публикует NSD-сервис и слушает TCP-порт signaling; Parent находит сервис через NSD и
  подключается.
- Signaling — TLS 1.2+ с mutual authentication; сертификат peer принимается только при совпадении
  SHA-256(SubjectPublicKeyInfo) с fingerprint доверенного устройства (pinning); глобальное
  отключение TLS validation запрещено. Fallback, если Keystore-key нельзя использовать в TLS:
  эфемерный TLS-сертификат + ECDSA identity assertion, подписанный Keystore identity, с тем же
  pinning; ослабление аутентификации не допускается.
- Media — WebRTC audio-only (SIPSorcery), Opus, DTLS-SRTP; ICE — только host candidates в LAN.
- Медиа и аудиобуферы не записываются, не логируются и не отправляются никуда, кроме peer.

### Угрозы и митигации (signaling/media)

| Угроза | Митигация | Тест |
|---|---|---|
| MITM/подмена peer при подключении | TLS + pinning identity из trusted store; недоверенный peer отклоняется до SDP | contract tests: неверный fingerprint, чужой сертификат |
| Replay/повтор соединения | TLS session bind + nonce/session id; одноразовый handshake; повторный не даёт нового доверия | replay tests |
| Downgrade протокола | строгая версия signaling/media; unsupported_version → typed error, соединение не устанавливается | version tests |
| Cleartext signaling | запрещён; только TLS; architecture test на отсутствие cleartext-сокетов/HTTP | grep/arch test, review |
| Отключение TLS validation | запрещено; pinning-проверка явная, без DangerousAcceptAnyServerCertificateValidator | architecture/security test |
| Перехват/запись media | DTLS-SRTP (шифрование), без custom crypto; аудио не пишется на диск и не логируется | review + architecture test (нет File/Socket кроме adapter), no-audio-log test |
| DoS/flood в LAN | ограниченные очереди/буферы, таймауты handshake, drop-oldest для audio, лимиты размера signaling | unit tests bounds/timeout |
| Потеря control-канала | heartbeat независимо от media; ParentLossAlarm локально; reconnect policy | unit tests (005 phase 1) + integration |
| Media path без звука | media health (packets/duration) отдельно от transport connected; typed degraded/stalled | health tests + device test владельцем |
| AP isolation/guest Wi-Fi | typed error без утечки секретов; device-проверка 007 | device BLOCKED |
| Компрометация trusted store | вне scope 005; integrity/rollback — 008 | residual |
| Утечка SDP/приватных IP в production logs | логирование только counters/типов; SDP не логируется | secret-log architecture test |

### Failure modes

- TLS/pinning failure → соединение отклонено, typed error (unauthorized_peer/tls_failure), trust не
  меняется; секреты не логируются.
- Signaling timeout/peer недоступен → typed error, reconnect по policy.
- DTLS/media не поднялись → control может быть Connected, media = Stalled; UI показывает
  «нет звука», ParentLossAlarm срабатывает по локальному таймауту.
- Ручной Stop → reconnect loop отменяется, ресурсы (TLS, WebRTC, audio) освобождаются.
- Потеря сети/смена IP → network callback отменяет старые попытки; discovery/resolve заново.

### Residual risks (005 phase 2)

- **Слышимость подтверждена владельцем 2026-11-08** на Huawei nova Y90 + nova 13 Pro (Android 12,
  v0.3.1): pairing, MediaFlowing, звук, reconnect Wi-Fi, screen-off, Stop. Задержка/echo оценены
  субъективно (без жалоб); полная OEM-матрица, звонки/audio focus, BT/route, 8h soak, 100 циклов,
  battery drain — 007 (docs/operations/device-test-results-2026-11-08.md).
- ICE host-only: сети с client isolation/разными VLAN не поддерживаются (typed error; 007).
- Jitter buffer/Opus параметры — стартовые измеряемые настройки, не финальные; пересмотр по device
  метрикам.
- LAN availability: rogue mDNS/TCP connect+close может временно помешать старту; bounded retry/
  rate-limit handshake — 007 (H3/H5), trust/pinning не обходится.
- Control heartbeat (v0.4.0): ping/pong идёт по уже аутентифицированному TLS control-каналу; потеря
  pong > 15 s → typed heartbeat_missed/Degraded/Reconnecting; ParentLossAlarm (15 s) поднимает
  локальную тревогу. Обе стороны должны быть обновлены; старый peer не отвечает pong → heartbeat_missed.
  Rate-limit/backoff неудачных handshake и тюнинг порогов по device-метрикам — 007.
- Trusted store — private SharedPreferences без tamper-evidence; компрометация файла потребует
  pairing/UI решения (008/007).
- Android SslStream + ephemeral mutual TLS проверены на 2 Huawei/Android 12; 13+/14+ и OEM
  security-политики — 007.
- Если SIPSorcery не пройдёт расширенную device matrix — замена адаптера на libwebrtc (ADR-0005),
  Domain/Application не меняются.