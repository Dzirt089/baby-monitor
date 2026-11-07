# Implementation Plan: 005-session-reliability

**Spec**: [spec.md](./spec.md) | **Status**: In Progress | **Created**: 2026-11-07

## Phases

1. **Phase 1 (этот PR)**: Domain MonitoringSession/State/Health/DisconnectReason; Application
   ReconnectPolicy, SessionHealthMonitor, ParentLossAlarm; unit-тесты с fake TimeProvider; 005 docs.
2. **Phase 2**: SIPSorcery media adapter за IRealtimeTransport в Infrastructure (PackageReference
   >= 10.0.17, ADR-0005) + contract suite; Android AudioCapture/AudioPlayback adapters
   (AudioRecord/AudioTrack, bounded buffers, role invariants); use case wiring/reconnect loop с
   IConnectivityMonitor/telemetry.
3. **Проверки**: local gates; emulator install/launch; device matrix (100 циклов, 8h soak, Wi-Fi loss,
   звонки, audio focus) — **BLOCKED: нет реальных устройств**.

## Technical Notes

- КА: один switch на переходы; нарушения → InvalidSessionTransitionException; terminal Failed/Idle
  (stopped) не перезапускается.
- Backoff: delay = min(initial * factor^(attempt-1), max) * (1 + (unit(attempt)*2-1)*jitter);
  unit — детерминированный hash номера попытки (без Random), sleep — вызывающий слой.
- Health: control Connected/Degraded/Lost по heartbeatTimeout; media Flowing/Stalled по mediaTimeout;
  IsUserHearingAudio требует оба.
- ParentLossAlarm: локальный порог; до первого ReportPeerAlive не поднимается (fail-safe против
  ложной тревоги до старта media).
- SIPSorcery adapter — только Infrastructure; Application видит IRealtimeTransport; contract suite
  обязателен; adapter добавлен в phase 2.
- Android audio: bounded PCM buffers, drop-oldest; никакой записи на диск; Parent без AudioRecord,
  Child без AudioTrack.

## Risks

| Риск | Митигация |
|---|---|
| Reconnect storm | exponential backoff + jitter + cap + max attempts; manual stop отменяет |
| Ложная/поздняя тревога | локальный TimeProvider, порог, независимость от Child, тесты |
| Health не отражает слышимость | IsUserHearingAudio только control+media; device BLOCKED |
| SIPSorcery утечёт в Application | port + architecture tests + contract suite |
| Аудио в логах | запрет payload/секретов, только counters (phase 2) |
| Device-поведение не проверено | честный BLOCKED, 007 device hardening |

## Gate

Phase 2 (media/audio) не считается device-проверенной без реальных устройств; end-to-end и soak —
BLOCKED.