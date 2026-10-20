# ADR-0007: Локальная безопасность — pairing, identity, signaling и media

- **Статус**: Proposed (требуется threat model в 004)
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор + security review
- **Связанные документы**: [AGENTS.md](../../../AGENTS.md) раздел 10, [ADR-0005](0005-media-transport-library.md)

## Контекст

Локальная Wi-Fi сеть считается недоверенной. Требуется первичное сопряжение двух устройств,
доверенные повторные подключения, аутентифицированный signaling, защищённый media (DTLS-SRTP),
хранение долгосрочных private keys в Android Keystore, одноразовые короткоживущие tokens с защитой
от replay, отсутствие cleartext signaling и отключения TLS validation. Собственная криптография
запрещена. Модель угроз (кто атакует: сосед в сети, вредоносная точка доступа, украденное
устройство) должна быть зафиксирована до выбора схемы.

## Решение (предлагаемое к проверке)

- Identity: долгосрочная пара ключей на устройство, private key — Android Keystore (non-exportable
  где возможно); публичная identity/fingerprint участвует в pairing.
- Pairing: Child создаёт одноразовый short-lived token и QR, содержащий protocol version, token и
  fingerprint публичной identity (без private key); устройства подтверждают владение ключами и
  показывают одинаковый короткий verification code; пользователь подтверждает; peer сохраняется
  в trusted store; повторные подключения — только от доверенного peer.
- Signaling: аутентифицирован и привязан к paired device; TLS с проверкой сертификата (без
  DangerousAcceptAnyServerCertificateValidator); cleartext HTTP/WebSocket запрещён.
- Media: DTLS-SRTP (WebRTC), без custom encryption.
- Tokens: одноразовые, с TTL, anti-replay (nonce/counter), не логируются.
- mDNS TXT: без имени ребёнка, владельца, постоянного device id, ключей и pairing secret.

Конкретная схема (например, mutual TLS с self-signed identity certificate + pinning fingerprint,
или WebRTC identity/datachannel-аутентификация) выбирается в 004 после threat model; spike
обязан показать failure modes и тесты.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Статус |
|---|---|---|---|
| Пиннинг self-signed сертификата + mutual TLS | зрелые TLS-примитивы, pinning | нет PKI/revocation, нужен аккуратный provisioning | кандидат |
| WebRTC identity/certificate pinning | минимум доп. инфраструктуры | меньше стандартизированной аутентификации signaling | кандидат |
| Trust on first use по IP/mDNS имени | просто | spoofing, MITM, нарушает AGENTS.md | отклонено |
| Облачный IdP/PKI | удобно | вне LAN-сценария, privacy, зависимость | отклонено |

## Последствия

- Security-sensitive feature: обязательны threat model, failure modes, тесты replay/expiration,
  review изменений pairing/key storage/TLS/transport.
- Потерянное устройство удаляется из trusted store; re-pairing — явным действием пользователя.
- Ключи и секреты не покидают устройство; логи не содержат key material/fingerprints целиком.

## Проверка и критерии пересмотра

004: threat model + матрица тестов (подмена peer, replay token, истёкший token, MITM, гостевая
сеть). Пересмотр — при обнаружении weakness или изменении media-стека (ADR-0005).
