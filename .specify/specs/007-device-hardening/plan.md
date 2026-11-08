# Implementation Plan: 007 — Device hardening & field reliability

**Spec**: [spec.md](spec.md) · **Status**: In Progress (device pass 2026-11-08 частично выполнен)

## Технический подход

1. **Reliability code (Application/Infrastructure, без новых пакетов)**
   - Heartbeat: расширить signaling-протокол ping/pong (уже есть) до периодического цикла в
     `SipsorceryRealtimeTransportSession`/`TlsSignalingConnection`: отправка ping с интервалом
     options, pong → событие/таймастер; `IRealtimeTransportSession` дополнить heartbeat-событием
     (Application-порт, без Android). `MarkHeartbeat` в health monitor; отдельный
     `realtime.heartbeat_missed` от media stall.
   - ParentLossAlarm: вынести evaluator в Application (таймер через TimeProvider), вызвать из
     coordinator/FGS; тревога локальна, не требует сети; UI/уведомление — typed состояние, без аудио.
   - Telemetry: `ISessionTelemetry`/агрегаты (counters, без per-frame логов), адаптер —
     Platform.Android/Infrastructure; диагностика отображает агрегаты (без секретов).
   - LAN hardening H3/H5: bounded retry Accept/перебор candidates в coordinator; лимит неудачных
     handshake + backoff на listener; bind signaling/ICE к Wi-Fi/Ethernet (H5/LOW-9).
2. **Field-проверки**: чек-листы calls/BT/route/Doze/TalkBack; прогон на 2 Huawei + по возможности
   Pixel/Samsung/Xiaomi (если появятся у владельца/у агента); фиксировать PASS/FAIL/PARTIAL/IOS.
3. **Soak/perf**: 8h soak + 100 connect/disconnect; метрики через adb dumpsys/собственные counters;
   battery без зарядки; профилирование allocations/CPU (dotnet-counters недоступны на Android —
   использовать dumpsys/собственные counters).
4. **A11y**: TalkBack-проход основных экранов; contentDescription/touch targets; исправления в App.
5. **Release**: v0.4.0 (minor: новые возможности heartbeat/alarm/telemetry) или patch-релизы; тег,
   GitHub Release, обновление CHANGELOG/threat model/roadmap; публичная дистрибуция — 008/owner.

## Затрагиваемые слои

- Application: IRealtimeTransportSession heartbeat-событие/контракт, coordinator alarm/telemetry,
  health/typed errors, unit-тесты с fake TimeProvider.
- Infrastructure: ping-цикл, counters в signaling/media adapters, bind-адреса, rate-limit.
- Platform.Android: FGS/notification alarm state, diagnostic counters view, permissions/Doze/
  battery-adapters при необходимости; audio focus/route только device-проверки, без больших правок.
- App: диагностика/уведомления/строки тревоги, a11y.
- Docs: device results, threat model residual, ADR update (heartbeat semantics), CHANGELOG.

## Тесты

- Unit: heartbeat timeout/regain, ParentLossAlarm (fake TimeProvider), counters (детерминированные),
  LAN retry/backoff, role invariants не ломаются.
- Contract: ping/pong цикл loopback, teardown/dispose, отсутствие утечек.
- Device: 007 checklist (calls/BT/route/Doze/soak/100 циклов/TalkBack/camera).
- Architecture: новые типы не протаскивают Android в Application; counters без аудио/секретов.

## Риски

- OEM Huawei агрессивно убивает FGS: возможны BLOCKED/PARTIAL; политика — документировать whitelist
  как условие, не как «корректность» (AGENTS §9).
- Heartbeat в signaling может конфликтовать с SIPSorcery control pump — реализовать через
  существующий control-канал с таймаутами, без второго TCP.
- 8h soak/100 циклов — времязатратно; выполнять в фоне сбора метрик, не блокируя релиз-фиксы.
- Полная OEM-матрица может остаться BLOCKED (нет устройств) — честно фиксировать.

## Критерии готовности плана

- Все AC 007 имеют evidence (PASS/FAIL/PARTIAL/IOS/BLOCKED с причиной); никаких заявленных
  непроверенных device-AC; релиз по предохранителям (review + CI + tag).
