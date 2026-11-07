# Implementation Plan: 002-device-discovery

**Spec**: [spec.md](./spec.md) | **Status**: In Progress | **Created**: 2026-11-07

## Phases

1. **Phase 1 (выполнена)**: порты IPeerDiscovery/IPeerAdvertiser/ILocalNetworkPermission, типы
   сессий, состояний и ошибок в Application; PeerTxtRecords (allowed keys); fake + contract tests
   в Infrastructure.Tests; unit-тесты Application (TXT/privacy/валидация).
2. **Phase 2 (выполнена)**: NSD discover/advertise/resolve adapter в Platform.Android,
   multicast lock, permission adapter (ACCESS_LOCAL_NETWORK, API 36/37), manifest permissions,
   connectivity precondition (Wi-Fi/Ethernet), API-34+ ServiceInfoCallback.
3. **Phase 3**: local gates + emulator smoke (install/launch; discovery без UI/инструментации
   неприменим), device matrix BLOCKED (нет устройств), PR → CI → независимое ревью → merge.

Адаптеры пока не включены в composition root (App): подключение и permission host появятся в
003/006 вместе с UI/сервисом. До этого экспорт типов из Platform.Android не используется приложением.

## Technical Notes

- Service type: `_babymonitor._tcp`; TXT keys: `v` (protocol version), `role` (child).
- В Application нет NSD/Android типов; endpoint — immutable TransportEndpoint из Realtime порта.
- Endpoint не identity; после pairing (004) доверие проверяется по ключам, IP/mDNS не доверяем.
- ACCESS_LOCAL_NETWORK: runtime permission; на API < 36 не требуется, на 36+ проверяется/запрашивается
  через Activity; реализация скрыта за ILocalNetworkPermission.
- Multicast lock: acquire при discovery/advertise, release в Stop/Dispose (always).
- NSD callbacks после Stop: generation/cts guard, отмена listeners.

## Risks

| Риск | Митигация |
|---|---|
| mDNS не работает на emulator/гостевой сети | smoke честно документируется; device tests BLOCKED |
| NSD callbacks приходят после Stop | generation/cts guard, отмена listeners, тесты на Stop |
| Privacy leak через TXT | unit-тест allowed-keys + review |
| Permission differences между API levels | permission adapter + architecture tests |
| API 34+ Host deprecation | HostAddresses на 34+, legacy Host под pragma CA1422 |
| Adapter не в composition до 003/006 | зафиксировано в plan/tasks; integration в 003/006 |

## Стратегия проверки реального NSD-адаптера

Contract suite (`BabyMonitor.Infrastructure.Tests`, net10.0) не может ссылаться на
`BabyMonitor.Platform.Android` (net10.0-android), поэтому в CI он гоняет только fake-реализации.
Реальный NSD-адаптер проверяется: (a) сборкой Platform.Android и всего solution без warnings;
(b) architecture tests (manifest permissions/no microphone/platform isolation); (c) независимым
review; (d) device tests — **BLOCKED: нет реальных устройств** (нужен net10.0-android test host
или ручной прогон на телефоне). Чистая логика (TXT wire-format, валидация endpoint/роли) вынесена
в Application и покрыта unit-тестами. Это ограничение зафиксировано в PR и отчёте, а не выдаётся
за полную проверку.

## Gate

Device-проверки не заявляются выполненными без реальных устройств. Pairing/доверие — только 004.