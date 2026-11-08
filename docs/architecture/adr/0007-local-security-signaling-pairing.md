# ADR-0007: Локальная безопасность — pairing, identity, signaling и media

- **Статус**: Accepted (2026-11-08: вариант A — mutual TLS + pinning self-signed identity, media — SIPSorcery DTLS-SRTP; владелец подтвердил в режиме «максимальная автономия с предохранителями»)
- **Дата**: 2026-10-20 (обновлено 2026-11-08)
- **Решение принимает**: владелец продукта + ведущий архитектор; security review — в PR 005 phase 2
- **Связанные документы**: [AGENTS.md](../../../AGENTS.md) раздел 10, [ADR-0005](0005-media-transport-library.md),
  [pairing threat model](../../security/pairing-threat-model.md), [spec 005](../../../.specify/specs/005-session-reliability/spec.md)

## Контекст

Локальная Wi-Fi сеть считается недоверенной. Требуется первичное сопряжение двух устройств,
доверенные повторные подключения, аутентифицированный signaling, защищённый media (DTLS-SRTP),
хранение долгосрочных private keys в Android Keystore, одноразовые короткоживущие tokens с защитой
от replay, отсутствие cleartext signaling и отключения TLS validation. Собственная криптография
запрещена. Модель угроз (сосед в сети, вредоносная точка доступа, украденное устройство) зафиксирована
в 004 (docs/security/pairing-threat-model.md); 006 phase 2 завершила первичное pairing офлайн через QR.

## Решение (принято, вариант A)

- **Identity**: долгосрочная пара ключей ECDSA P-256 на устройство, private key — Android Keystore
  (non-exportable), публичная identity/fingerprint — SHA-256 SubjectPublicKeyInfo. Pairing и
  trusted store уже реализованы (004/006) и переиспользуются без изменений семантики.
- **Signaling**: TCP-соединение в LAN, защищённое TLS 1.2+ с взаимной аутентификацией и **pinning**
  удостоверения peer: сертификат peer принимается только если SHA-256(SubjectPublicKeyInfo)
  совпадает с fingerprint доверенного устройства из trusted store. Никаких CA/PKI, никакого
  глобального отключения TLS validation и DangerousAcceptAnyServerCertificateValidator;
  hostname-проверка заменяется явным pinning по сохранённой identity (допустимо только для
  self-signed paired peer, зафиксировано здесь и в threat model).
- **Identity proof в TLS**: первичная схема — self-signed сертификат на базе долгосрочной identity;
  если .NET SslStream на Android не может использовать non-exportable Keystore private key
  (проверяется spike/contract tests в 005 phase 2), допускается fallback: TLS с эфемерным
  сертификатом для шифрования + app-layer identity assertion, подписанный Keystore identity
  (ECDSA P-256) над nonce/транскриптом, с тем же pinning по trusted store. Ослабление
  аутентификации peer не допускается; выбор фиксируется в коде адаптера и тестах.
- **Signaling protocol**: версионированный бинарный/текстовый протокол только по TLS: hello с
  protocol version, session id/nonce, peer identity assertion; SDP/ICE-кандидаты и control
  messages; unknown/недоверенный peer отклоняется до обмена SDP; replay защищён nonce и
  одноразовостью соединения; tokens/секреты не логируются.
- **Media**: SIPSorcery (PackageReference >= 10.0.17, ADR-0005) в Infrastructure за
  IRealtimeTransport: WebRTC audio-only, Opus, **DTLS-SRTP**; ICE — только host candidates в LAN
  (STUN/TURN вне scope MVP); медиа не записывается и не логируется.
- **Audio**: Child — только AudioRecord (capture), Parent — только AudioTrack (playback); audio
  focus и routing для Parent обязательны; Parent не запрашивает RECORD_AUDIO.
- **Reliability**: heartbeat control-канала и media health проверяются независимо; reconnect policy,
  health monitor и ParentLossAlarm из 005 phase 1 используются без дублирования логики; ручной Stop
  прекращает reconnect loop.
- **mDNS TXT**: без имени ребёнка, владельца, постоянного device id, ключей и pairing secret
  (уже реализовано в 002).

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Статус |
|---|---|---|---|
| A. Пиннинг self-signed сертификата + mutual TLS | зрелые TLS-примитивы, pinning по trusted store | нет PKI/revocation, нужен аккуратный TLS-код | **принято** |
| B. WebRTC identity/certificate pinning | минимум доп. инфраструктуры | меньше стандартизированной аутентификации signaling | отклонено для MVP |
| Trust on first use по IP/mDNS имени | просто | spoofing, MITM, нарушает AGENTS.md | отклонено |
| Облачный IdP/PKI | удобно | вне LAN-сценария, privacy, зависимость | отклонено |
| Собственный криптопротокол/шифрование | «просто» | запрещено AGENTS.md, нет review | отклонено |

## Последствия

- Security-sensitive feature: обязательны threat model, failure modes, тесты pinning/MITM/replay/
  downgrade, review изменений pairing/key storage/TLS/transport.
- Потерянное устройство удаляется из trusted store; re-pairing — явным действием пользователя.
- Ключи и секреты не покидают устройство; логи не содержат key material, полных fingerprints,
  SDP с приватными адресами/кандидатами на уровне production logs.
- AP/client isolation и guest Wi-Fi не поддерживаются: typed-ошибка и понятное сообщение
  (device-проверки — 007).
- SIPSorcery — единственный media-стек; при провале device matrix допускается замена адаптера на
  thin binding Android libwebrtc без изменения Domain/Application (ADR-0005).

## Проверка и критерии пересмотра

005 phase 2: contract/unit тесты (pinning, недоверенный peer, replay nonce, downgrade protocol
version, обрыв TLS, heartbeat), emulator/device smoke на двух устройствах владельца.

**Статус проверки (2026-11-08)**: на реальных Huawei nova Y90 (Child) + nova 13 Pro (Parent),
Android 12, v0.3.1 — mutual TLS + pinning по trusted store, identity assertion, DTLS-SRTP/Opus и
приём звука работают end-to-end (MediaFlowing на обоих, Parent AudioTrack started); слышимость
подтверждена владельцем. Детали — docs/operations/device-test-results-2026-11-08.md. Остаточные
риски (OEM/rate-limit/8h soak, Android 13+/14+, trusted-store tamper-evidence) — 007/008.

Пересмотр — при обнаружении weakness, смене media-стека (ADR-0005) или решении о публичном
распространении (шифрование/signaling вне LAN).