# Feature Specification: 002-device-discovery

**Feature Branch**: 002-device-discovery
**Created**: 2026-11-07
**Status**: In Progress (phase 1 — контракт и contract suite; phase 2 — Android NSD adapter и permissions — реализованы; emulator smoke неприменим без UI/инструментации, device matrix BLOCKED)
**Input**: AGENTS.md разделы 8, 10; roadmap 002

## Контекст

Child публикует сервис в локальной Wi-Fi сети, Parent обнаруживает его и получает transport
endpoint. mDNS TXT не содержит персональных данных, ключей и pairing secrets. IP-адрес не является
идентификатором и не сохраняется как постоянный endpoint. Discovery и permissions изолируются от
Application платформенным адаптером (Platform.Android).

## User Scenarios

### US-1: Parent обнаруживает Child (P1)

Parent запускает поиск в текущей LAN; при появлении Child приходит peer с endpoint и ролью.
При смене сети/IP устаревший endpoint отбрасывается: discovery/resolve выполняются заново.

**Acceptance**: сессия IPeerDiscovery отдаёт peer-ов; Stop освобождает NSD/multicast ресурсы;
типизированные ошибки для «Wi-Fi нет», «сервис не найден», «resolve не выполнен».

### US-2: Child публикует сервис (P1)

Child запускает advertise `_babymonitor._tcp`; TXT содержит только protocol version и роль;
после Stop сервис снимается, multicast lock освобождён.

### US-3: Permissions и privacy (P1)

Platform permission-адаптер запрашивает только необходимые разрешения и при отказе возвращает
типизированную ошибку; Parent не получает RECORD_AUDIO из-за discovery.

## Functional Requirements

- **FR-001**: Порты IPeerDiscovery, IPeerAdvertiser, ILocalNetworkPermission и immutable типы
  сессий/состояний/ошибок в Application, без Android/NSD-типов.
- **FR-002**: Ручные fake-реализации + общий contract suite (start/stop, идемпотентность, отмена,
  события состояния, завершение потока peer-ов) в Infrastructure.Tests.
- **FR-003**: Platform.Android-адаптер NSD/DNS-SD: advertise, discover, resolve, multicast lock,
  освобождение ресурсов; service type и TXT-ключи — из единых констант Application.
- **FR-004**: mDNS TXT: только `v` (protocol version, целое 1..CurrentProtocolVersion) и `role`
  (wire-формат — исходное имя `Child`; числовые/иные значения отклоняются). Запрещены имя ребёнка,
  владелец, постоянный device ID, ключи, pairing token/secret, fingerprint. Display name не персональный.
- **FR-005**: Типизированные ошибки: Wi-Fi отсутствует, сервис не найден, resolve не выполнен,
  permission denied, NSD unavailable, invalid TXT.
- **FR-006**: Все async-операции принимают CancellationToken; Stop/Dispose освобождают NsdManager,
  listeners и multicast lock; fire-and-forget запрещён.
- **FR-007**: Минимальные manifest permissions; runtime permission запрашивается только при
  необходимости и скрыт за ILocalNetworkPermission; Parent не получает microphone permission.

## Non-Functional Requirements

- **NFR-001**: Contract suite проходит в CI без реальной сети/Android.
- **NFR-002**: Application не получает новых пакетов; NSD-типы не протекают за Platform.Android.
- **NFR-003**: Тесты не используют реальный mDNS; emulator smoke допускается только как smoke.

## Acceptance Criteria

- [x] AC-001: Порты и типы в Application; сборка без warnings.
- [x] AC-002: Fake + contract suite зелёный (Application 68, Infrastructure 37 tests) и включён в CI.
- [x] AC-003: NSD-адаптер в Platform.Android компилируется (0 warnings); architecture tests
  проверяют manifest permissions, отсутствие microphone permissions и изоляцию platform-типов.
- [x] AC-004: mDNS TXT проходит unit-тест allowed-keys (никаких персональных данных/секретов).
- [x] AC-005: Manifest permissions минимальны (INTERNET, ACCESS_NETWORK_STATE,
  CHANGE_WIFI_MULTICAST_STATE, ACCESS_LOCAL_NETWORK); permission-адаптер возвращает типизированный
  permission_denied; microphone permissions отсутствуют (проверяется architecture test).
- [ ] AC-006: Emulator smoke (advertise/discover) выполнен или честно задокументирован как
  неприменимый; **device matrix BLOCKED: нет реальных устройств** (guest Wi-Fi/AP isolation/VLAN,
  смена IP, два разных телефона — только на железе).

## Out of Scope

Pairing/security handshake, TLS/signaling, media transport, UI, foreground service, reconnect-политика.
Discovery не даёт доверия peer по IP/mDNS: доверие — только после pairing (004).

## Dependencies & Assumptions

- Android NSD (NsdManager), targetSdk 36, minSdk 24; WifiManager multicast lock;
  ACCESS_LOCAL_NETWORK runtime permission (enforced с Android 17/targetSdk 37; на API 36 — opt-in).
- 2 реальных Android-устройства в одной LAN для device-проверок (НЕТ; BLOCKED).
- Emulator API 35 — только smoke, не доказательство LAN-поведения.