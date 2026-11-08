# Implementation Plan: 005-session-reliability (phase 2)

**Spec**: [spec.md](./spec.md) | **ADR**: [ADR-0007](../../../docs/architecture/adr/0007-local-security-signaling-pairing.md) Accepted (2026-11-08, вариант A), [ADR-0005](../../../docs/architecture/adr/0005-media-transport-library.md) (SIPSorcery >= 10.0.17)
**Status**: Phase 1 merged (PR #12); phase 2 in progress | **Created**: 2026-11-07 (обновлён 2026-11-08)

## Phases

1. **Phase 1 (merged, PR #12)**: Domain MonitoringSession/State/Health/DisconnectReason; Application
   ReconnectPolicy, SessionHealthMonitor, ParentLossAlarm; unit-тесты.
2. **Phase 2a — signaling/TLS/pinning** (этот PR): Application-порты для listener/audio; Infrastructure
   signaling (TCP + TLS 1.2+, mutual auth, pinning identity из trusted store, versioned hello/nonce,
   SDP exchange); contract/security tests (недоверенный peer, replay, version mismatch, cleartext
   отсутствует).
3. **Phase 2b — media (SIPSorcery)**: WebRTC audio-only, Opus, DTLS-SRTP за IRealtimeTransport;
   contract suite; ICE host-only; telemetry counters; no audio in logs/disk.
4. **Phase 2c — Android audio**: AudioRecord (Child capture), AudioTrack (Parent playback), audio
   focus/route для Parent, bounded channels, role invariants; platform adapters за Application-портами.
5. **Phase 2d — Application orchestration**: use case/coordinator, связывающий discovery → TLS →
   media → audio в foreground service; reconnect loop с ReconnectPolicy/IConnectivityMonitor; Stop
   освобождает ресурсы; telemetry wiring.
6. **Phase 2e — проверки и релиз**: unit/contract/arch tests, build/format/vuln, независимое ревью,
   PR → CI → merge → тег v0.3.0 → GitHub Release; чек-лист device-тестов владельцу.
7. **Phase 3 — device-прогоны владельца**: 2 устройства (слышимость, выключенный экран, потеря
   Wi-Fi/reconnect, звонок/focus, route change, battery) — результаты фиксируются; расширенная
   OEM-матрица/8h soak — 007.

## Технические решения (phase 2)

- **Порты Application** (новые, presentation-agnostic):
  - IRealtimeTransport (есть, 001) — initiator (Parent); ConnectAsync(endpoint).
  - IRealtimeTransportListener — Child: StartAsync(port) → ожидание одного доверенного peer;
    возвращает IRealtimeTransportSession. Contract suite расширяется на listener.
  - IAudioCapture — Child: старт захвата, IAsyncEnumerable/событие кадров PCM (bounded), Stop/Dispose.
  - IAudioPlayback — Parent: старт воспроизведения, WriteAsync(PCM) (bounded), Stop/Dispose.
  - IConnectivityMonitor — network callbacks → отмена устаревших попыток/reconnect.
- **Composition root**: адаптер IRealtimeTransport/Listener создаётся с ожидаемой identity peer
  (fingerprint из trusted store) и IPairingKeyStore/identity; endpoint — только адрес. Application
  не видит SIPSorcery/Android.
- **TLS**: mutual TLS 1.2+, pinning SHA-256(SPKI) peer-сертификата к trusted fingerprint; hostname
  verification заменена pinning (только paired self-signed peer); никакого глобального отключения
  TLS validation. Fallback при невозможности использовать Keystore key в SslStream: эфемерный
  TLS-сертификат + ECDSA identity assertion (nonce/transcript) с тем же pinning; фиксируется в
  ADR-0007 и тестах.
- **Signaling**: версионированный протокол поверх TLS (hello/session nonce/identity assertion,
  offer/answer, control ping/pong, close); unknown version/peer → typed error до SDP; таймауты
  handshake; лимиты размера сообщений.
- **Media**: SIPSorcery WebRTC audio-only (Opus 48 kHz mono, 20 ms); DTLS-SRTP; ICE host-only;
  jitter buffer — в стеке; drop-oldest для очередей; audio frames не логируются/не сохраняются.
- **Opus codec**: определить при реализации: возможности SIPSorcery 10.0.17 / Concentus /
  Android MediaCodec; выбор зафиксировать (пакет — только после ADR/license/vuln). Кодирование/
  декодирование не в Application.
- **Android audio**: AudioRecord VOICE_COMMUNICATION/MIC по умолчанию — измеряемая настройка;
  Parent — AudioTrack, audio focus (AUDIOFOCUS_GAIN_TRANSIENT_MAY_DUCK? для радионяни — gain),
  routing (speaker/headset/BT) и обработка focus loss (пауза/тревога) — по device-тестам.
- **Foreground service**: сеанс живёт в MonitoringForegroundService (003); Activity только
  отображает снимки; Stop освобождает TLS/WebRTC/Audio и отменяет reconnect loop.
- **Reliability**: heartbeat control + media health независимо; SessionHealth/ParentLossAlarm
  (phase 1) используются; network callback отменяет старые попытки; correlation ids в логах.

## Риски

| Риск | Митигация |
|---|---|
| TLS/pinning ошибка → ложный отказ подключения | contract/security tests: верный/неверный fingerprint, fallback identity assertion; device smoke |
| Android Keystore key нельзя использовать в SslStream | заранее проверить spike; fallback по ADR-0007 с тем же pinning |
| SIPSorcery не тянет WebRTC/DTLS-SRTP на Android | contract suite + emulator/device smoke; при провале — libwebrtc binding (ADR-0005), Domain/Application не меняются |
| Opus codec недоступен/лицензии | проверить SIPSorcery/Concentus/MediaCodec; пакеты — ADR/license/vuln; запасной вариант MediaCodec |
| Эхо/задержка/audio focus | device-тесты владельца; настройки измеряемые, не скрытые |
| Аудио в логах/на диске | architecture tests, review, no-audio-log checks |
| Утечка секретов/SDP | typed errors, counters only; secret-log tests |
| Android 14+ FGS ограничения для media/mic | использовать типы microphone/mediaPlayback, permission flows (003), device-прогон владельца |
| AP/client isolation | typed error; 007 |

## Gate

Код/тесты/CI green и независимое ревью — обязательны для merge. Реальная слышимость и device-AC —
только после прогона владельца на двух устройствах по чек-листу v0.3.0; до этого приложение не
объявляется работающей радионяней. Полная OEM-матрица/8h soak — 007.
