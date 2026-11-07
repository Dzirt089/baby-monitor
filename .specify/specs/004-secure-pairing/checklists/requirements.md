# Requirements Quality Checklist: 004-secure-pairing

- [x] CHK-001 Scope pairing ограничен; signaling/media/UI вне scope.
- [x] CHK-002 Threat model покрывает MITM/replay/QR leak/Keystore/lost device.
- [x] CHK-003 QR не содержит private key/session secret; token one-time + expiry.
- [x] CHK-004 Подписи обеих сторон и ECDH проверяются; verification code совпадает.
- [x] CHK-005 Доверие сохраняется только после явного подтверждения обоих пользователей.
- [x] CHK-006 Trusted store содержит только публичные данные; Forget удаляет.
- [x] CHK-007 Секреты не попадают в логи/ошибки/trusted store (тесты).
- [x] CHK-008 Только BCL crypto + Android Keystore; нет custom crypto.
- [ ] CHK-009 Application без Android-типов (architecture tests).
- [x] CHK-010 Replay/expiry/tamper/malformed QR покрыты тестами.
- [-] CHK-011 End-to-end pairing и Keystore на реальных устройствах — **BLOCKED**.
- [ ] CHK-012 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|