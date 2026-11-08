# Feature Specification: 004-secure-pairing

**Feature Branch**: 004-secure-pairing
**Created**: 2026-11-07
**Status**: Done (2026-11-08). Контракты/протокол/тесты + Android Keystore/trusted store; end-to-end pairing на 2 реальных Huawei: одинаковый verification code, trusted store обоих, DER/P1363 interop. Camera↔camera на реальных устройствах — 007.
**Input**: AGENTS.md раздел 10; roadmap 004; docs/security/pairing-threat-model.md

## Контекст

Локальная Wi-Fi сеть недоверенная. Первичное сопряжение Child и Parent должно дать обоим
устройствам уверенность в identity друг друга и сохранить доверенного peer для повторных
подключений. Канал pairing (signaling) появится в 005; в 004 реализуются криптографический
протокол, QR-артефакты и trusted store, которые 005 будет использовать.

## User Scenarios

### US-1: Child создаёт приглашение (P1)

Child генерирует одноразовый pairing token, показывает QR с protocol version, token, публичной
identity и эфемерным ключом. QR живёт ограниченное время и не содержит private key.

### US-2: Parent принимает приглашение (P1)

Parent сканирует QR, проверяет версию/подпись/срок, строит ответ со своей публичной identity и
эфемерным ключом; обе стороны получают одинаковый короткий verification code.

### US-3: Пользователи подтверждают код (P1)

Оба пользователя видят одинаковый 6-значный код и подтверждают. При отказе/несовпадении доверие
не сохраняется. После подтверждения PairedDevice сохраняется в trusted store (только публичные
данные).

### US-4: Повторные подключения только от доверенного peer (P1, runtime в 005)

Trusted store позволяет проверить, что входящий peer — ранее сопряжённое устройство; signaling и
media должны аутентифицироваться по этой identity (005/DTLS-SRTP).

## Functional Requirements

- **FR-001**: Долгосрочная identity — ECDSA P-256; private key хранится только в Android Keystore
  (adapter Platform.Android в phase 2); наружу доступны SubjectPublicKeyInfo и SHA-256 fingerprint.
- **FR-002**: QR содержит protocol version, одноразовый token (256-bit), expiresAt, публичную
  identity Child, эфемерный ECDH-ключ Child и подпись Child; private keys и session secrets в QR
  запрещены.
- **FR-003**: Token одноразовый, живёт <= 5 минут; повторное использование отклоняется
  `pairing.token_reused`, истёкший — `pairing.expired`. Одноразовость обеспечивает Child-сессия
  (issued token); Parent может принять тот же QR повторно в пределах срока, но доверие требует
  подтверждения Child. Persistent replay-store для signaling — 005.
- **FR-004**: Parent проверяет подпись Child и срок; Child проверяет подпись Parent; обе стороны
  выводят shared secret по ECDH P-256 и verification code (6 цифр) через HKDF-SHA256 над
  транскриптом; код на обеих сторонах совпадает.
- **FR-005**: Доверие сохраняется только после явного подтверждения пользователем обоих устройств
  (`pairing.user_rejected` иначе); сохраняются только публичная identity, fingerprint, роль
  peer и время.
- **FR-006**: Типизированные ошибки: invalid_qr, unsupported_version, expired, token_reused,
  invalid_signature, code_mismatch, user_rejected, store_failed, invalid_state.
  identity_mismatch зарезервирован для привязки identity в signaling (005): в 004 identity
  восстанавливается только из валидированного SPKI, fingerprint вычисляется из SPKI.
- **FR-007**: Секреты (token, private keys, shared secret, session key) не логируются и не попадают
  в сообщения об ошибках; QR payload считается секретом.
- **FR-008**: Только проверенная криптография: BCL System.Security.Cryptography + Android Keystore;
  собственная криптография запрещена. Signaling в 005 обязан быть аутентифицированным (TLS/DTLS),
  cleartext запрещён.
- **FR-009**: Удаление доверенного устройства (ForgetPairedDevice) удаляет запись из trusted store;
  повторные подключения к нему требуют нового pairing.

## Non-Functional Requirements

- **NFR-001**: Протокол покрыт unit/contract-тестами: happy path, replay, expiry, tamper подписи,
  malformed QR, несовпадение кода, отказ пользователя, удаление trusted device.
- **NFR-002**: Domain/Application не содержат Android-типов; Keystore/SharedPreferences — только
  Platform.Android.
- **NFR-003**: Эмуляторный smoke + реальное сопряжение двух Huawei (paste-fallback) выполнено
  2026-11-08; camera↔camera — 007.

## Acceptance Criteria

- [x] AC-001: Application-контракты и протокол реализованы, сборка без warnings.
- [x] AC-002: Unit-тесты протокола (happy/replay/expiry/tamper/QR/код/отказ/SPKI/версия) зелёные.
- [x] AC-003: Android Keystore identity adapter + trusted store adapter реализованы; architecture
  tests подтверждают отсутствие Android-типов в Application; device pass 2026-11-08.
- [x] AC-004: QR/ошибки не содержат секретов; тесты проверяют отсутствие private key material.
- [x] AC-005: Полное сопряжение двух реальных устройств — PASS 2026-11-08 (QR paste-fallback,
  одинаковый code, trusted store обоих; camera↔camera — 007).
- [-] AC-006: Keystore/pairing проверены на 2 Huawei/Android 12; другие OEM/API 13+/14+ — **007**.

## Delta 006 phase 2 (2026-11-07)

Emulator smoke pairing UI выявил интероп-дефект, не видимый на BCL fakes: Android Keystore
`SHA256withECDSA` возвращает подпись в DER SEQUENCE (~70–72 байта), а BCL `ECDsa.VerifyData`
по умолчанию ожидает IEEE P1363 (r||s, 64 байта для P-256) → реальное сопряжение отклонялось
`pairing.invalid_signature` при валидных ключах. Исправление: `PairingCrypto.TryVerifySignature`
принимает обе стандартные кодировки (BCL overloads `DSASignatureFormat`), семантика протокола не
меняется; добавлены regression tests `PairingSignatureInteropTests` (DER Child, DER Parent,
обе стороны DER, tampered DER). Device E2E — PASS 2026-11-08 на двух Huawei (paste-fallback,
  одинаковый code, trusted store); camera↔camera — 007.

## Out of Scope

Signaling transport (005), DTLS-SRTP media (005), UI QR-рендеринг/сканирование (006),
persistent storage hardening/backup политики (008), аттестация/StrongBox (007).

## Dependencies & Assumptions

- Android Keystore API (EC P-256) — phase 2; BCL ECDsa/ECDH/HKDF/SHA-256.
- TimeProvider инжектируется; QR payload формат версионирован.
- 2 реальных устройства для end-to-end pairing отсутствуют — BLOCKED.