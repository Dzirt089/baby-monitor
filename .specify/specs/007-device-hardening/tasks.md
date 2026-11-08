# Tasks: 007 — Device hardening & field reliability

**Статусы**: [ ] pending · [x] done · [-] blocked (причина обязательна)

## Phase 1 — Reliability code (Application/Infrastructure)

- [x] T001 Heartbeat: periodic ping/pong в signaling-сессии + HeartbeatReceived в
  IRealtimeTransportSession; SessionHealthMonitor.MarkHeartbeat (v0.4.0, 498/498 tests).
- [x] T002 Typed `realtime.heartbeat_missed` отдельно от media stall + first-heartbeat watchdog;
  integration в coordinator (Degraded/Reconnecting), unit-тесты с fake TimeProvider.
- [x] T003 ParentLossAlarm: evaluator (TimeProvider) в coordinator + snapshot.ParentAlarmActive;
  high-importance alarm-уведомление/статус «Связь потеряна!»; unit-тесты (device-проверка — T027b).
- [x] T004 Telemetry: MediaSessionTelemetry counters (send/recv кадры, reconnect attempts, uptime,
  last media frame) в snapshot; без аудио/SDP/fingerprint/IP (underruns/silence — 007 follow-up).
- [x] T005 Диагностика: строка метрик в Diagnostics screen; arch-тесты на отсутствие секретов/аудио.
- [ ] T006 H4: прямые тесты TlsPeerCertificateValidator (CA/expired/EKU/RSA), клиентский
  unsupported-version ack, SDP 256 KiB лимит; semantic arch-test SslStream/Tcp.
- [ ] T007 H5/LOW-9: bounded retry Accept/перебор discovered candidates; rate-limit/backoff
  неудачных handshake; bind signaling/ICE к Wi-Fi/Ethernet; tests.
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

## Verification Log

| Дата | Проверка | Результат |
|---|---|---|
| 2026-11-08 | v0.4.0 reliability code gates | heartbeat/ParentLossAlarm/telemetry реализованы+тесты: 498/498 (Domain 38, Application 258, Infrastructure 145, Architecture 57); Debug/Release 0 warnings; format clean; vuln OK |
| 2026-11-08 | Автоматизированный 100-цикловый Wi-Fi reconnect на v0.3.1 | 8 циклов PASS (~27-28 s восстановление), затем harness/Wi-Fi OEM деградация → typed Failed после исчерпания policy; тест остановлен, НЕ заявляется пройденным (нужна новая методика/состояние сети) |
| 2026-11-08 | Device pass v0.3.1 на Huawei nova Y90 + nova 13 Pro (Android 12) | pairing/PASS, audibility owner-confirmed/PASS, MediaFlowing/PASS, reconnect/PASS, screen-off 12s/PASS, Stop/PASS; battery/BT/calls/TalkBack/OEM — NOT RUN/BLOCKED; см. docs/operations/device-test-results-2026-11-08.md |