# Результаты device-прогона 0.3.1 (2026-11-08)

**Прогон выполнен агентом по разрешению владельца** на двух его физических устройствах, подключённых
через USB/adb; владелец подтвердил слышимость. Скриншоты/логи не коммитятся (в них могут быть
посторонние уведомления); ниже — обезличенная сводка. Это не заменяет полную OEM-матрицу 007.

## Устройства и окружение

| Роль в тесте | Модель | Android | API | Сборка | IP / сеть |
|---|---|---|---|---|---|
| Child | HUAWEI nova Y90 (CTR-L21) | 12 | 31 | 13.0.0.298(C10E2R4P2) | 192.168.3.x, один Wi-Fi LAN |
| Parent | HUAWEI nova 13 Pro (MIS-LX9) | 12 | 31 | EMUI/HarmonyOS build (Android 12) | 192.168.3.y, тот же LAN/BSSID |

- APK: GitHub Release **v0.3.1**, `io.github.dzirt089.babymonitor-Signed.apk`,
  SHA-256 `43c4a90aad288c7a14ef5de2b78db7cd6240a2a4f2deb7d429b69510ceb6caaf` (совпал с checksum релиза);
  versionCode 301 / versionName 0.3.1 на обоих.
- Сопряжение: диагностический QR-fallback (камера↔камера не автоматизировалась): invite QR Child
  декодирован на хосте, вставлен в Parent; response QR Parent декодирован и вставлен в Child;
  оба устройства показали одинаковый 6-значный verification code; подтверждение на обоих; trusted store:
  Child и Parent видят peer с короткими fingerprint в диагностике (полные fingerprints в отчёт не
  переносятся).
- Мониторинг запущен на обеих ролях; Parent — mediaPlayback, Child — microphone; экраны устройства
  в foreground, USB-отладка включена.

## Методы снятия evidence (adb, без секретов)

- AudioTrack: `adb shell dumpsys audio | grep AudioPlaybackConfiguration` — state=started,
  usage=USAGE_MEDIA, content=CONTENT_TYPE_SPEECH, uid/pid приложения (сессия 345/345).
- Crash buffer: `adb shell logcat -d -b crash -t 40` — пусто на обоих после сценариев.
- Permissions Parent: `adb shell dumpsys package io.github.dzirt089.babymonitor | grep RECORD_AUDIO` —
  `granted=false` после Parent-сессии (CAMERA=false); Child получает разрешение по действию Start.
- Состояния UI/уведомлений: `adb shell uiautomator dump` + `dumpsys notification --noredact`
  (фиксировались тексты «Звук передаётся», Media: MediaFlowing; аудио/секретов в отчёте нет).
- Логи/скриншоты в репозиторий не коммитятся (privacy; в них могут быть посторонние уведомления).

## Результаты

| # | Проверка | Результат |
|---|---|---|
| 1 | Установка/запуск release APK, versionName 0.3.1 | PASS (оба) |
| 2 | Сопряжение офлайн QR (paste-fallback), одинаковый verification code 848016, trusted store на обоих | PASS |
| 3 | Старт Child + Parent, FGS-уведомления «Радионяня: ребёнок/родитель» | PASS |
| 4 | Соединение signaling/TLS/DTLS-SRTP через реальный Wi-Fi LAN; оба устройства Media: **MediaFlowing** | PASS |
| 5 | Parent AudioTrack: state=started, USAGE_MEDIA/CONTENT_TYPE_SPEECH, audio focus запрошен | PASS |
| 6 | **Слышимость Child→Parent подтверждена владельцем** («Да, слышу звук»), без отчёта о треске/эхе | PASS (owner confirmation) |
| 7 | Reconnect: Wi-Fi Parent disabled 5 с → enabled; оба: MediaFlowing → Connecting → MediaFlowing автоматически (~9 с, backoff policy) | PASS |
| 8 | Выключенный экран Child 12 с: сессия продолжается, Parent AudioTrack started, уведомления «Звук передаётся»; после включения экрана MediaFlowing | PASS |
| 9 | Чистый Stop на обоих: UI Stopped/Media не запущена, уведомления сняты, AudioTrack остановлен | PASS |
| 10 | Crash buffer пуст на обоих; ANR/исключений приложения не зафиксировано | PASS |
| 11 | Parent **не** запрашивает RECORD_AUDIO: после Parent-сессии `RECORD_AUDIO: granted=false` (CAMERA=false); Child — разрешение запрошено при Start | PASS (API31; 13+/14+ permission flow — 007) |
| 12 | Батарея/температура за ~8 мин тестов (на USB-питании): nova Y90 76 %/31 °C, nova 13 Pro 70 %/35 °C — **не** валидный battery-drain тест (зарядка по USB) | PARTIAL |
| 13 | Входящий звонок/audio focus loss, Bluetooth/наушники, смена маршрута | NOT RUN (007, нужен владелец) |
| 14 | 100 reconnect-циклов, 8 h soak, battery drain без зарядки, TalkBack, camera↔camera | NOT RUN (007) |
| 15 | Guest Wi-Fi/AP isolation, второй OEM-класс (Pixel/Samsung/Xiaomi) | NOT RUN (007) |

## Выводы

- Критический AC «реальная передача звука Child → Parent на двух устройствах» **выполнен**:
  кодовая база 005 phase 2 работает end-to-end на реальных Huawei Android 12, включая mutual
  TLS + pinning, DTLS-SRTP/Opus, AudioRecord/AudioTrack, FGS, reconnect и экран-off.
- Остаются 007-проверки: звонки/focus loss, Bluetooth/наушники, battery/8h soak/100 циклов,
  TalkBack, camera-сканирование, OEM-матрица, guest/AP isolation; control heartbeat/ParentLossAlarm
  и telemetry counters — код 007 (см. tasks H1/H2/H5).
- Приложение по-прежнему не медицинское изделие; выводы ограничены двумя устройствами Huawei с
  Android 12 и одним LAN.


## v0.4.0 device smoke (2026-11-08, оба Huawei на adb)

**APK**: GitHub Release v0.4.0, SHA-256 `7cf680d4ebc70b9b12d778a468c2e48f8cb37ecdb25a5ac0abfb01d162287327`, versionName 0.4.0/versionCode 400 (оба).
Установка поверх v0.3.1 (trusted store сохранён), pairing не потребовался.

| # | Проверка | Результат |
|---|---|---|
| 1 | Установка/запуск v0.4.0, старт Child+Parent, MediaFlowing на обоих | PASS |
| 2 | Diagnostics: строка «Метрики: кадры ↑/↓, reconnect, uptime» присутствует | PASS, но метрики/uptime в открытом экране не обновляются без смены состояния (найден дефект: snapshot с telemetry не переопубликовывается периодически; после Failed State показал ↑11168/uptime 4:04) |
| 3 | Heartbeat/media alarm: Wi-Fi Child disabled → Parent в течение ≤3 s показал «Связь потеряна!», high-importance notification «Радионяня: связь потеряна» (отдельный канал), Media: Reconnecting | PASS (alarm), но Parent остался в Reconnecting >60 s после возврата Wi-Fi Child (найден дефект: discovery-ожидание повторного подключения без таймаута, MaxAttempts не исчерпывается) |
| 4 | Child после Wi-Fi off/on: typed Failed `discovery.wifi_unavailable` после reconnect-попыток, counters/reconnect=5/uptime в Diagnostics | PASS (typed error) |
| 5 | Crash buffer на обоих после сценариев | пуст |
| 6 | Terminal Failed Parent standalone alarm (после остановки FGS), звук/вибрация/DND alarm-канала | NOT RUN (Parent не дошёл до Failed из-за п.3; будет проверено после фикса) |
| 7 | Media-only loss alarm (control жив, кадры стоят) | NOT RUN |
| 8 | 20/100 Wi-Fi reconnect циклов с новой методикой | NOT RUN (тестовая методика дорабатывается) |

## v0.4.4 two-device retest (T040/T043, partial, 2026-11-08)

Оба Huawei на adb, v0.4.4 (versionCode 404).

| # | Проверка | Результат |
|---|---|---|
| 1 | Install/launch v0.4.4 на обоих, старт Parent+Child (Child теперь ждёт Parent — T046) | PASS: обе MediaFlowing |
| 2 | Diagnostics live telemetry | PASS: Child «кадры ↑14775 ↓0 · reconnect 0 · uptime 00:05:00», Parent «↑0 ↓14827 · uptime 00:05:12» (version 0.4.4) — счётчики/uptime живые |
| 3 | Alarm при Wi-Fi Child off | PASS: Parent alarm ≤5 s («Связь потеряна!», notification «Радионяня: связь потеряна»), затем terminal Failed `realtime.signaling_timeout` через bounded retries (~105–120 s); FGS остановлен, standalone alarm id=1004 (channel parent_alarm, importance HIGH, category=err) остаётся; Child Failed typed `discovery.wifi_unavailable` |
| 4 | Screen-off 90 s | PASS: обе flowing по нотификациям, crash buffer пуст |
| 5 | Recovery после включения Wi-Fi | PASS только после ручного рестарта: обе MediaFlowing |
| 6 | **Auto-reconnect 10 s Wi-Fi flap** | **FAIL (T053)**: оба сеанса терминально Failed за 139 s, ручной рестарт обязателен; alarm Parent остаётся |
| 7 | Media-only alarm, звук/вибрация/DND alarm-канала, calls/BT/TalkBack, 100 reconnect cycles, 8 h soak | NOT RUN |

Privacy: device test — без аудиоданных; alarm-нотификация не содержит PII. Sound/vibration не подтверждены
человеком (только channel importance/category).

## v0.4.7 T053 retest: 10 s Wi-Fi flap auto-recovery (PASS, 2026-11-08)

Оба Huawei на adb, v0.4.7 (versionCode 407), старт Parent+Child, обе MediaFlowing.

| # | Сценарий | Результат |
|---|---|---|
| 1 | Child Wi-Fi off 10 s → on | PASS: auto-recovery обеих MediaFlowing через 26 s от начала flap (16 s после включения Wi-Fi), без ручного рестарта, alarm Parent appeared во время outage и снялся после восстановления |
| 2 | Ещё 3 цикла 10 s off/on | PASS: 26 s / 29 s / 26 s, без terminal Failed, без crash |
| 3 | Crash buffers после | пусты |

**Вывод:** T053 фикс подтверждён на устройстве (auto-reconnect переживает 10 s Wi-Fi flap).

### v0.4.4 rogue/flood single-device (nova Y90, no Parent, 2026-11-08): versionCode 404 install PASS;
Child Advertising/Media Connecting, listener 0.0.0.0:47800. Host (192.168.3.x) сделал 3 rogue TCP
connect+close → Child остался Connecting (retry Accept/cooldown), notification «Подключение…», crash
buffer пуст. После 5 суммарных rogue-подключений (bounded attempts exhausted) — terminal Failed
`realtime.tls_failure`, последующие TCP connect time-out (сервис остановлен). Это подтверждает bounded
resilience и **остаточную DoS-возможность**: активный LAN-атакующий 5+ подряд может сорвать initial
start (known limitation H3/T052, trust/pinning не обходится).

**v0.4.3 single-device sanity (nova Y90, no Parent, 2026-11-08):** versionCode 403 install PASS;
Child-сеанс без Parent: «Подключение…», Running/Media Connecting >20 s, без Failed/crash, Stop PASS.
Порядок Starting-before-RunAsync (T047) подтверждён косвенно успешным стартом; двухдевайсовые
T040/T043 — pending.

**T046 retest v0.4.2 (single-device, nova Y90, no Parent, 2026-11-08):** install versionCode 402 PASS;
Child-сеанс без Parent остаётся Running/Media Connecting >40 s (10 замеров по 4 s), terminal
signaling_timeout не наступает; notification «Радионяня: ребёнок»; crash buffer пуст; Stop PASS.
Это подтверждает device-фикс T046; двухдевайсовые сценарии (live counters, terminal standalone alarm,
media-only, sound/vibration/DND) — T040/T043 после возвращения Parent.

**Найденные дефекты (fix → v0.4.1):** (a) telemetry snapshot не обновляется периодически — исправлено
live-refresh 1 s; (b) reconnect attempt в discovery/connect без таймаута → бесконечный Reconnecting при
исчезнувшем peer — исправлено reconnectAttemptTimeout 20 s (retry) → terminal Failed; (c) alarm для
terminal Failed Parent срабатывает для любых ошибок — исправлено scoping loss-кодов. Device-retest v0.4.1
(install, live counters, terminal Failed standalone alarm) — выполняется, результаты будут добавлены.

**Не заявляется:** звук/вибрация alarm-канала, terminal-Failed standalone alarm, media-only alarm —
пока не проверены; слышимость v0.3.1 остаётся подтверждённой ранее.


### Single-device smoke v0.4.1 (Child nova Y90, Parent phone у владельца)

После установки v0.4.1 (versionCode 401, SHA `1cfe0498a99dec2c6993e253d1fd59e80d53bca920db047461f70ab772610218`):
install/launch/role/UI PASS, crash buffer пуст. Но запуск Child-сеанса **без Parent**: через ~10 s
terminal Failed `realtime.signaling_timeout` вместо ожидания Parent (Advertising). Причина —
`TlsSignalingServerSession.AcceptAsync` применял HandshakeTimeout к TCP-ожиданию клиента. Исправление — v0.4.2 (T046): таймаут только после TCP accept (TLS+hello), ожидание peer без таймаута
(tests: pending accept > HandshakeTimeout, late peer accepted, cancel/dispose paths). Device-retest v0.4.2
на оставшемся nova Y90: Child без Parent должен оставаться Advertising — выполняется после установки. На v0.4.1 двухдевайсовая сессия ранее поднималась (v0.4.0 smoke), но этот сценарий
блокировал запуск Child до Parent.