# Tasks: 007 — Device hardening & field reliability

**Статусы**: [ ] pending · [x] done · [-] blocked (причина обязательна)

## Phase 1 — Reliability code (Application/Infrastructure)

- [x] T001 Heartbeat: periodic ping/pong в signaling-сессии + HeartbeatReceived в
  IRealtimeTransportSession; SessionHealthMonitor.MarkHeartbeat (v0.4.0, 502/502 tests).
- [x] T002 Typed `realtime.heartbeat_missed` отдельно от media stall + first-heartbeat watchdog;
  integration в coordinator (Degraded/Reconnecting), unit-тесты с fake TimeProvider.
- [x] T003 ParentLossAlarm: evaluator (TimeProvider) в coordinator + snapshot.ParentAlarmActive;
  high-importance alarm-уведомление/статус «Связь потеряна!»; unit-тесты (device-проверка — T040).
- [x] T004 Telemetry (частично): MediaSessionTelemetry counters (send/recv кадры, reconnect attempts,
  uptime, last media frame) в snapshot; без аудио/SDP/fingerprint/IP. Lost/underruns/silence/buffer
  depth/encode-decode — 007 follow-up.
- [x] T005 Диагностика: строка метрик в Diagnostics screen; arch-тесты на отсутствие секретов/аудио.
- [x] T006 H4 (v0.4.2): прямые тесты TlsPeerCertificateValidator (CA/expired/EKU/RSA), клиентский
  unsupported-version ack, SDP 256 KiB лимит; semantic arch-test SslStream/Tcp.
- [ ] T007 H5/LOW-9: bounded retry Accept/перебор discovered candidates; rate-limit/backoff
  неудачных handshake; bind signaling/ICE к Wi-Fi/Ethernet; tests.
  - [x] T007a (v0.4.4) Application peer resilience: Child Accept retry до 5 с паузой 250 ms; Parent
    candidate enumeration до 5 в одной discovery-сессии; fixed async-iterator dispose bug; tests.
  - [x] T007b (v0.4.4) Infrastructure failed-handshake cooldown 250 ms (SignalingOptions), успешный
    accept без задержки, Stop/Dispose/cancel не зависают; 5 tests.
  - [ ] T007c Bind signaling/ICE к Wi-Fi/Ethernet интерфейсу (Application port + Android adapter + options).
- [x] T008 Локальные гейты: Debug/Release build 0/0, тесты 498/498, format clean, vuln scan чистый.
- [ ] T009 Независимое review (security-sensitive: heartbeat/DoS/bind) + findings.
- [ ] T010 PR → зелёный CI → merge; тег v0.4.0 (minor — новые возможности) или patch.

## Phase 2 — Field checks (2 устройства владельца, adb)

- [x] T011 Установка v0.3.1 на Huawei nova Y90 + nova 13 Pro, versionName 0.3.1, runtime smoke.
- [x] T012 Сопряжение офлайн QR (paste-fallback): одинаковый code 848016, trusted store обоих.
- [x] T013 Слышимость Child→Parent: MediaFlowing на обоих, Parent AudioTrack started,
  **владелец подтвердил звук**.
- [x] T014 Reconnect: Wi-Fi off/on 5 с → Connecting → MediaFlowing автоматически (~9 с).
- [x] T015 Экран выключен (Child, 12 с): передача продолжается, уведомления «Звук передаётся».
- [x] T016 Чистый Stop: AudioTrack остановлен, уведомления сняты, UI Stopped.
- [x] T017 Parent не запрашивает RECORD_AUDIO (API31; permission flow 13+/14+ — T024).
- [-] T018 Входящий звонок / audio focus loss/regain — **BLOCKED: нужен входящий звонок на тестовом
  телефоне; не автоматизируется без риска для владельца**.
- [-] T019 Bluetooth/наушники/route change — **BLOCKED: нужны BT-наушники у устройства**.
- [-] T020 Расширенный screen-off >1 ч / Activity destroyed — частично T015; длительная проверка в T028.
- [-] T021 TalkBack-проход — **BLOCKED: нужен человек/TalkBack-сессия; эмулятор не заменяет**.
- [-] T022 Camera↔camera pairing — **BLOCKED: камеры не автоматизируются; в v0.3.1 проверен
  paste-fallback + камера на эмуляторе (006)**.
- [-] T023 Battery drain без зарядки 30–60 мин — **PARTIAL/BLOCKED: телефоны были на USB-питании;
  нужен отдельный прогон без зарядки**.
- [x] T024 Android 12 permission flows (FGS microphone/mediaPlayback, уведомления) — PASS на Huawei.
- [-] T025 Android 13+/14+ restrictions — **BLOCKED: нет устройств с API 33+**.
- [ ] T026 Guest Wi-Fi/AP isolation — нужен роутер/сеть; typed ошибка ожидается.

## Phase 3 — Soak / perf

- [ ] T027 8-часовой soak: memory/latency/стабильность сессии; логи/метрики через counters.
- [ ] T028 100 connect/disconnect/reconnect циклов без утечек (включая screen-off).
- [ ] T029 Battery/CPU/temperature/allocations на слабом устройстве (без зарядки); профиль
  Opus/VOICE_COMMUNICATION; результаты в docs/operations.
- [-] T030 OEM-матрица Pixel/Samsung/Xiaomi/Redmi — **BLOCKED: нет устройств; минимум Huawei/Honor
  проверен, полная матрица — по мере появления устройств**.

## Phase 4 — A11y / UX robustness

- [ ] T031 TalkBack-проход: роль, pairing, мониторинг, диагностика; исправления contentDescription/
  touch targets/порядка фокуса.
- [ ] T032 Camera↔camera pairing на двух реальных устройствах; отказ CAMERA — typed ошибка.
- [ ] T033 Activity destroyed при живом service; Stop из уведомления освобождает ресурсы.

## Phase 5 — Docs / release

- [ ] T034 Обновить docs/operations/device-test-results-YYYY-MM-DD.md по каждому прогону
  (PASS/FAIL/PARTIAL/IOS, модели/API, APK hash).
- [ ] T035 Обновить threat model residual risks (heartbeat/DoS/bind/OEM) и ADR-0007 heartbeat
  semantics при необходимости.
- [ ] T036 CHANGELOG/roadmap/README: status 007, известные ограничения, следующий шаг 008.
- [ ] T037 Релиз: review + CI + tag/GitHub Release (по предохранителям); публичная дистрибуция — 008.

## Phase 6 — UX observations (device pass)

- [ ] T038 FGS-сессия живёт независимо от Activity (это by design), но UX должен это объяснять:
  на экране выбора роли показывать «Сессия активна» с кнопками «Вернуться к мониторингу» и
  «Остановить»; не позволять менять роль без Stop; Back из MonitoringActivity — подтверждение/
  понятное состояние. Наблюдение владельца 2026-11-08: звук продолжал идти на Parent, пока открыт
  экран выбора роли (Parent микрофон не использует — проверено RECORD_AUDIO=false).
- [ ] T039 Проверить, что при выборе другой роли во время активной сессии UI не создаёт вторую
  сессию/не теряет управление; typed error/подсказка.

## Phase 7 — device-проверка (T040/T043, v0.4.4 partial)

- [-] T040 (v0.4.1 retest pending) Проверить на 2 устройствах: heartbeat/pong, срабатывание ParentLossAlarm «Связь потеряна!»
  (heartbeat loss и media-only loss), звук/вибрация/DND alarm-канала, terminal Failed Parent alarm,
  телеметрия в Diagnostics. — **BLOCKED: adb nova 13 Pro пропал после reboot (нужно подтвердить
  USB-отладку); после восстановления выполнить.**

## Phase 8 — follow-ups после v0.4.1

- [-] T042 (v0.4.2, частично) Live-refresh 1/s переопубликовывает SnapshotChanged → FGS Notify и UI Render каждую секунду;
  оптимизировать (обновлять уведомление только при изменении текста/флага; UI — при изменении
  отображаемых полей), измерить battery/CPU во время 8h soak.
- [-] T043 (partial, v0.4.4) Device-retest: live counters PASS, terminal Failed standalone alarm PASS
  (Parent id=1004, importance HIGH, category=err, FGS stopped), screen-off 90 s PASS, recovery после
  ручного рестарта PASS; media-only alarm, звук/вибрация/DND, calls/BT/TalkBack — NOT RUN.
- [ ] T044 Setup-timeout tests: Child reconnect Accept timeout, «timeout после успешного setup не срабатывает»,
  race network-change vs setup-timeout (Stale vs SetupFailure).
- [x] T046 (v0.4.2, device PASS single-device) Child listener ждёт Parent: HandshakeTimeout только после
  TCP accept; device-retest v0.4.2 на nova Y90 без Parent: Media Connecting >40 s, без Failed, crash buffer пуст.
- [x] T047 (v0.4.3) Starting event order gate; Regression: slow subscriber/Stop during Starting.
- [ ] T050 Parent candidate wait 250 ms может быть мал для медленного NSD resolve — тест slow-discovery/
  увеличить/ждать до deadline попытки (LOW PR #24).
- [x] T051 (v0.4.5) Async-iterator cleanup: non-OCE pending MoveNext не маскирует typed ошибку,
  dispose best-effort; regression-тест.
- [x] T053 (v0.4.7, device-retest pending) Auto-reconnect после Wi-Fi flap: LAN-gate + network-loss
  retries (60 s/250 ms/5), MaxAttempts не расходуется; tests +5; device evidence v0.4.4 FAIL → retest v0.4.7.
- [ ] T052 Bounded initial DoS: 5 подряд rogue-отказов (Child) / rogue mDNS candidates (Parent) всё ещё
  дают initial FailStartup — documented limitation; рассмотреть продолжать Advertising/сбрасывать окно
  (H3). Device evidence v0.4.4: 3 rogue → Connecting, 5 rogue → Failed tls_failure.
- [ ] T045 Product decision: discovery-loss коды дают «Связь потеряна!» и на initial-попытке (ребёнок не
  найден) — решить, нужен ли отдельный текст/без alarm для initial setup.
- [x] T048 (v0.4.5) Строгий порядок Starting при конкурентном Stop: StopAsync ждёт Starting-публикацию
  (bounded 5 s) до Stopping; XML-doc SnapshotChanged contract; regression-тест.
- [x] T049 (v0.4.5) Semantic arch-test расширен: Socket/UdpClient/SslStream/HttpClient/... только
  Infrastructure/Signaling|Realtime; word-boundary Socket + matcher test.

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-08 | v0.4.4 device rogue/flood (single nova Y90) | 3 rogue TCP connect+close → Child Connecting (retry/cooldown), 5 → terminal FailStartup tls_failure (bounded, residual DoS H3/T052); crash buffer пуст; rate-limit cooldown device-observation PASS-partial |
| 2026-11-08 | v0.4.7 gates | 560/560 (Domain 38, Application 291, Infrastructure 172, Architecture 59); Debug/Release 0 warnings; format clean; vuln OK; T053 network-aware reconnect |
| 2026-11-08 | v0.4.6 gates | 555/555 (Domain 38, Application 286, Infrastructure 172, Architecture 59); Debug/Release 0 warnings; format clean; vuln OK; T048 A/B; v0.4.5 tag orphan (Release failed) — v0.4.6 actual |
| 2026-11-08 | v0.4.5 gates | 552/552 (Domain 38, Application 283, Infrastructure 172, Architecture 59); Debug/Release 0 warnings; format clean; vuln OK; T048/T049/T051 |
| 2026-11-08 | v0.4.4 two-device T040/T043 (Huawei pair) | MediaFlowing PASS; live telemetry LIVE (↑14775/↓14827, uptime 5:00/5:12); Wi-Fi off → alarm ≤5 s, terminal Failed signaling_timeout + standalone alarm id1004 importance HIGH category err PASS; screen-off 90 s PASS; recovery после ручного рестарта PASS; **T053 auto-reconnect 10 s flap FAIL** (both terminal Failed за 139 s); sound/vibration/DND/calls/BT/TalkBack/100 cycles/8h — NOT RUN |
| 2026-11-08 | v0.4.4 gates | 549/549 (Domain 38, Application 281, Infrastructure 172, Architecture 58); Debug/Release 0 warnings; format clean; vuln OK; T007a/T007b |
| 2026-11-08 | v0.4.3 device sanity (single nova Y90) | install versionCode 403 PASS; Child без Parent: Running/Media Connecting >20 s, без Failed/crash, Stop PASS; двухдевайсовые T040/T043 pending |
| 2026-11-08 | v0.4.3 gates | 538/538 (Domain 38, Application 275, Infrastructure 167, Architecture 58); Debug/Release 0 warnings; format clean; vuln OK; Release-run Application tests 275/275; T047 event order |
| 2026-11-08 | v0.4.2 gates | 536/536 (Domain 38, Application 273, Infrastructure 167, Architecture 58); Debug/Release 0 warnings; format clean; vuln OK; T042/T006/T046 |
| 2026-11-08 | v0.4.1 gates | 513/513 (Domain 38, Application 273, Infrastructure 145, Architecture 57); Debug/Release 0 warnings; format clean; vuln OK |
| 2026-11-08 | v0.4.1 device smoke findings (v0.4.0) | Huawei pair: install/start/MediaFlowing PASS; alarm при Wi-Fi off PASS (≤3 s, high-importance notification); найдены дефекты: stale telemetry snapshot, бесконечный Reconnecting при исчезнувшем peer, alarm для локальных ошибок → исправлено в v0.4.1 (live refresh/reconnectAttemptTimeout/alarm scoping); device-retest v0.4.1 — T040 |
| 2026-11-08 | v0.4.0 reliability code gates | heartbeat/ParentLossAlarm/telemetry реализованы+тесты: 502/502 (Domain 38, Application 262, Infrastructure 145, Architecture 57); Debug/Release 0 warnings; format clean; vuln OK |
| 2026-11-08 | Автоматизированный 100-цикловый Wi-Fi reconnect на v0.3.1 | 8 циклов PASS (~27-28 s восстановление), затем harness/Wi-Fi OEM деградация → typed Failed после исчерпания policy; тест остановлен, НЕ заявляется пройденным (нужна новая методика/состояние сети) |
| 2026-11-08 | Device pass v0.3.1 на Huawei nova Y90 + nova 13 Pro (Android 12) | pairing/PASS, audibility owner-confirmed/PASS, MediaFlowing/PASS, reconnect/PASS, screen-off 12s/PASS, Stop/PASS; battery/BT/calls/TalkBack/OEM — NOT RUN/BLOCKED; см. docs/operations/device-test-results-2026-11-08.md |