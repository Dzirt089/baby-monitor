# Tasks: 005-session-reliability

Статусы: [ ] не начато, [x] завершено и проверено, [-] заблокировано (+причина).

## Phase 1 — Доменный КА и reliability-политики (merged, PR #12)

- [x] T001 MonitoringState/DisconnectReason/SessionHealth + MonitoringSession aggregate
- [x] T002 ReconnectPolicy (backoff+jitter+cap)
- [x] T003 SessionHealthMonitor (control/media независимо)
- [x] T004 ParentLossAlarm (локальная тревога Parent)
- [x] T005 Unit-тесты КА/переходов/health/backoff/alarm
- [x] T006 005 spec/plan/tasks/checklist/roadmap

## Phase 2a — Signaling/TLS/pinning (unblocked: ADR-0007 Accepted)

- [x] T007 ADR-0007 Accepted (вариант A) + threat model signaling/media + SIPSorcery PackageReference и транзитивные лицензии (THIRD-PARTY-NOTICES)
- [x] T008 Application: порты IRealtimeTransportListener, IAudioCapture, IAudioPlayback, IConnectivityMonitor, IAudioCodec (+ immutable contracts, XML-docs), DeviceIdentityPublicKey.VerifySignature
- [ ] T009 Infrastructure: TLS signaling (TCP, TLS 1.2+, mutual auth, pinning identity из trusted store, versioned hello/nonce, SDP exchange)
- [ ] T010 Contract/security tests signaling: недоверенный peer, replay, version mismatch, timeout, отсутствие cleartext/отключения TLS validation
- [ ] T011 Keystore-in-TLS spike: проверить, можно ли использовать non-exportable Keystore key в SslStream On Android; при невозможности — fallback identity assertion по ADR-0007 + тесты

## Phase 2b — Media (SIPSorcery)

- [ ] T012 SIPSorcery media adapter за IRealtimeTransport/Listener: WebRTC audio-only, Opus, DTLS-SRTP, ICE host-only
- [ ] T013 Contract suite для media-адаптера (общий для реализаций IRealtimeTransport); Opus encode/decode round-trip
- [ ] T014 Telemetry counters (packets, RTT, jitter, media silence, buffers, encode/decode) без аудио/секретов
- [ ] T015 Architecture tests: SIPSorcery только Infrastructure; отсутствие audio payload в логах/файлах

## Phase 2c — Android audio

- [ ] T016 Platform.Android: AudioRecord capture adapter за IAudioCapture (Child), bounded buffers, drop-oldest, typed errors
- [ ] T017 Platform.Android: AudioTrack playback adapter за IAudioPlayback (Parent), audio focus/route, focus loss, typed errors
- [ ] T018 Role invariants tests: Child без playback, Parent без RECORD_AUDIO/AudioRecord; permission flow
- [ ] T019 Emulator smoke: signaling/media/audio path на двух эмуляторах (если возможно) + install/launch

## Phase 2d — Application orchestration

- [ ] T020 Application: connection/media coordinator (discovery → TLS → media → audio) + reconnect/integration с SessionHealthMonitor/ParentLossAlarm/IConnectivityMonitor
- [ ] T021 Foreground service integration: сеанс в MonitoringForegroundService, Activity только snapshot; Stop освобождает ресурсы; cancellation/dispose
- [ ] T022 Typed errors mapping (unauthorized_peer/tls_failure/signaling_timeout/media_path_absent/capture/playback/network_lost/unsupported_version)

## Phase 2e — Проверки и релиз

- [ ] T023 Local gates: build Debug/Release, все тесты, format, vuln scan
- [ ] T024 Независимое ревью (fresh reviewer, security-sensitive) + закрытие findings
- [ ] T025 PR → зелёный CI (gh) → merge в main
- [ ] T026 Тег v0.3.0

### Follow-ups 007 (из security review PR #16)

- [ ] H1: control heartbeat: periodic ping/pong + SessionHealthMonitor.MarkHeartbeat + ParentLossAlarm
      в coordinate/FGS (FR-018/FR-021, AC-009).
- [ ] H2: telemetry counters (send/recv/lost, underruns, silence, buffer depth, encode/decode duration,
      reconnect count) через ISessionTelemetry/aggregates; без аудио/секретов (FR-022, T014).
- [ ] H3: listener rate-limit/backoff неудачных handshake (LAN DoS hardening, LOW-3);
      bind signaling/ICE кандидатов к Wi-Fi/Ethernet интерфейсу (LOW-9).
- [ ] H4: прямые тесты ветвей TlsPeerCertificateValidator (CA/expired/EKU/RSA), клиентский
      unsupported-version ack, SDP 256 KiB лимит (LOW-6); semantic arch-test для SslStream/Tcp
      (LOW-5). → GitHub Release (подписанный APK + SHA-256) + чек-лист слышимости/reconnect/battery владельцу

## Phase 3 — Device-проверки владельца (2 устройства)

- [ ] T027 Слышимость: Child → Parent, задержка, качество, громкость; выключенный экран
- [ ] T028 Reconnect: кратковременная потеря Wi-Fi, смена IP, входящий звонок/audio focus, route change
- [ ] T029 Battery/CPU: ориентировочный расход за сеанс 30–60 минут; 8h soak — 007
- [-] T030 Полная OEM-матрица (Pixel/Samsung/Huawei/Xiaomi), TalkBack, 8h soak, battery profiling — **BLOCKED: 2 устройства, полная матрица в 007**

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-07 | Phase 1 build/tests | Domain 38/38, Application 147/147, Architecture 45/45, Infrastructure 63/63; build 0 warnings; format OK |
| 2026-11-08 | ADR-0007 Accepted + spec/plan/tasks phase 2 | threat model обновлён; SIPSorcery разрешён; v0.2.0 (pairing UI) подготовлена для device-прогона владельцем |
| 2026-11-08 | Phase 2 implementation (commits 9ed4082..a381986) | signaling/TLS/pinning + SIPSorcery media + Opus codec + Android audio/connectivity + Application coordinator + FGS/UI интеграция; build Debug/Release 0 warnings, format clean, tests 471/471 (Domain 38, Application 245, Infrastructure 131, Architecture 57), vuln scan чистый; media loopback Child→Parent в Infrastructure tests (Opus кадр принят и декодирован); device-проверки — владелец (Huawei nova 13 Pro + Huawei nova Y90, adb недоступен) |
| 2026-11-08 | Phase 2 design/contracts (commit 9ed4082, cbe7bdd) | ADR-0007 Accepted; Application ports (listener/audio/codec/connectivity); SIPSorcery 10.0.17 + транзитивные лицензии; vuln scan чистый; Application build 0 warnings; параллельно: Infrastructure signaling/media/codec, Android audio/connectivity, Application coordinator — в работе |