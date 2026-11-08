# Результаты device-прогона 0.3.1 (2026-11-08)

**Прогон выполнен агентом по разрешению владельца** на двух его физических устройствах, подключённых
через USB/adb; владелец подтвердил слышимость. Скриншоты/логи не коммитятся (в них могут быть
посторонние уведомления); ниже — обезличенная сводка. Это не заменяет полную OEM-матрицу 007.

## Устройства и окружение

| Роль в тесте | Модель | Android | API | Сборка | IP / сеть |
|---|---|---|---|---|---|
| Child | HUAWEI nova Y90 (CTR-L21) | 12 | 31 | 13.0.0.298(C10E2R4P2) | 192.168.3.48, один Wi-Fi LAN |
| Parent | HUAWEI nova 13 Pro (MIS-LX9) | 12 | 31 | EMUI/HarmonyOS build (Android 12) | 192.168.3.64, тот же LAN/BSSID |

- APK: GitHub Release **v0.3.1**, `io.github.dzirt089.babymonitor-Signed.apk`,
  SHA-256 `43c4a90aad288c7a14ef5de2b78db7cd6240a2a4f2deb7d429b69510ceb6caaf` (совпал с checksum релиза);
  versionCode 301 / versionName 0.3.1 на обоих.
- Сопряжение: диагностический QR-fallback (камера↔камера не автоматизировалась): invite QR Child
  декодирован на хосте, вставлен в Parent; response QR Parent декодирован и вставлен в Child;
  оба устройства показали одинаковый код **848016**; подтверждение на обоих; trusted store:
  Child видит peer «родитель» с отпечатком a5706ebe, Parent — «ребёнок» d2007a60.
- Мониторинг запущен на обеих ролях; Parent — mediaPlayback, Child — microphone; экраны устройства
  в foreground, USB-отладка включена.

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
