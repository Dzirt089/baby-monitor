# Requirements Quality Checklist: 002-device-discovery

- [x] CHK-001 Scope discovery ограничен; pairing/transport/UI вне scope.
- [x] CHK-002 Каждый FR проверяем (артефакт/тест/документ).
- [x] CHK-003 Contract suite не зависит от Android/реальной сети и работает в CI.
- [x] CHK-004 mDNS TXT не содержит персональных данных, ключей и pairing secrets (тест allowed-keys).
- [x] CHK-005 Permissions минимальны (INTERNET, ACCESS_NETWORK_STATE, CHANGE_WIFI_MULTICAST_STATE,
  ACCESS_LOCAL_NETWORK); RECORD_AUDIO/audio permissions отсутствуют — проверяется architecture test.
- [x] CHK-006 Cancellation/cleanup (NsdManager, listeners, multicast lock) зафиксированы в контракте.
- [x] CHK-007 Platform-типы не протекают в Application; проверяется architecture tests.
- [x] CHK-008 Ошибки типизированы и различимы (wifi/service/resolve/permission/invalid_txt).
- [x] CHK-009 Device matrix: discovery/connect на 2 реальных Huawei PASS 2026-11-08; guest/AP isolation/VLAN — 007; emulator за device-тест не выдаётся.
- [x] CHK-010 Трассируемость FR → tasks.md.

## Review Record

| Дата | Ревьюер | Вердикт | Комментарии |
|---|---|---|---|
| | | | |