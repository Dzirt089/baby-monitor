# Requirements Quality Checklist: 002-device-discovery

- [x] CHK-001 Scope discovery ограничен; pairing/transport/UI вне scope.
- [x] CHK-002 Каждый FR проверяем (артефакт/тест/документ).
- [x] CHK-003 Contract suite не зависит от Android/реальной сети и работает в CI.
- [x] CHK-004 mDNS TXT не содержит персональных данных, ключей и pairing secrets (тест allowed-keys).
- [ ] CHK-005 Permissions минимальны; Parent не получает RECORD_AUDIO из-за discovery (phase 2).
- [x] CHK-006 Cancellation/cleanup (NsdManager, listeners, multicast lock) зафиксированы в контракте.
- [x] CHK-007 Platform-типы не протекают в Application; проверяется architecture tests.
- [x] CHK-008 Ошибки типизированы и различимы (wifi/service/resolve/permission/invalid_txt).
- [x] CHK-009 Device matrix честно BLOCKED (нет устройств); emulator не выдан за device-тест.
- [x] CHK-010 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|
| | | | |