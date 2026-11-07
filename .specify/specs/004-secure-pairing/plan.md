# Implementation Plan: 004-secure-pairing

**Spec**: [spec.md](./spec.md) | **Threat model**: [pairing-threat-model.md](../../../docs/security/pairing-threat-model.md)
**Status**: In Progress | **Created**: 2026-11-07

## Phases

1. **Phase 1 (этот PR)**: Domain/Application контракты (identity public key, PairedDevice,
   trusted store, key store, entropy), pairing protocol (QR, подписи, ECDH, verification code,
   replay/expiry, подтверждение), unit-тесты протокола и store.
2. **Phase 2 (следующий PR)**: Android Keystore adapter ECDSA P-256, Android trusted store
   (только публичные данные), architecture tests, документация/review.
3. **Проверки**: local gates; emulator install/launch; end-to-end pairing на двух устройствах —
   **BLOCKED: нет реальных устройств**.

## Protocol Notes

- Identity: ECDSA P-256 (Keystore); fingerprint = SHA-256(SubjectPublicKeyInfo), hex lowercase.
- Ephemeral: ECDH P-256 (BCL, in-memory per pairing session).
- Invitation transcript: version|token|expiresAt|childFingerprint|childEphemeralSpki;
  Child подписывает SHA-256(transcript).
- Response transcript: invitationTranscriptHash|parentFingerprint|parentEphemeralSpki;
  Parent подписывает SHA-256(responseTranscript).
- Shared secret: ECDH(childEphemeralPrivate, parentEphemeralPublic) == parent side.
- Verification code: HKDF-SHA256(sharedSecret, salt=SHA256(invitationTranscript|responseTranscript),
  info="babymonitor-pairing-v1-code", 4 bytes) -> mod 1_000_000 -> 6 digits.
- Token: 32 random bytes base64url; expiry 5 минут; single-use state machine.
- QR payload: "bm-pair:v1." + base64url(version|token|expiresAt|child SPKI|child ephemeral SPKI|
  signature); length-prefixed binary, JSON не используется (Application без System.Text.Json).
- Никаких private keys/shared secret в QR, логах, ошибках, trusted store.

## Risks

| Риск | Митигация |
|---|---|
| MITM в LAN | QR out-of-band + подписи + ECDH + user verification code |
| Replay token | 256-bit token, 5 минут, single-use state |
| Утечка QR | короткий срок жизни, one-time, без private keys |
| Keystore variations OEM | architecture tests + device BLOCKED (007) |
| Логи/ошибки с секретами | никаких секретов в Error/log; тесты на отсутствие private material |
| Downgrade/старые версии | protocol version pinned, unsupported_version |

## Gate

End-to-end pairing и Keystore на реальных прошивках не заявляются проверенными без устройств.