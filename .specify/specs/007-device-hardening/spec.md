# Feature Specification: 007 — Device hardening & field reliability

**Feature branch**: `007-device-hardening` (планируется)
**Status**: **In Progress** (часть проверок выполнена на реальных устройствах 2026-11-08, см.
[device-test-results-2026-11-08](../../../docs/operations/device-test-results-2026-11-08.md))
**Created**: 2026-11-08
**Зависит от**: 005 phase 2 (media/signaling — Done), 006 (UI/pairing — Done), ADR-0007 (Accepted)

## Контекст

v0.3.1 подтверждена владельцем на двух реальных Huawei (Android 12): сопряжение, передача звука,
MediaFlowing, слышимость, reconnect при Wi-Fi off/on, выключенный экран, чистый Stop. Это снимает
главный device-AC 005. Остаются риски, которые нельзя закрыть unit/loopback-тестами: OEM-ограничения
фоновой работы Huawei/Honor (агрессивная экономия батареи, Doze/App Standby), входящие звонки и
audio focus loss, Bluetooth/наушники/route change, длительный soak, battery drain без зарядки,
TalkBack/доступность, camera↔camera pairing, полная OEM-матрица, control heartbeat/ParentLossAlarm
и telemetry counters (отложены из 005 security review как H1/H2/H5).

## Scope

1. **Reliability code**: control heartbeat (ping/pong + `SessionHealthMonitor.MarkHeartbeat`),
   ParentLossAlarm в media-интеграции, typed «heartbeat missed» отдельно от media stall;
   telemetry counters/агрегаты (send/recv/lost, underruns, silence, buffer depth, encode/decode,
   reconnect count/duration, service lifetime) без аудио/секретов; LAN availability hardening
   (bounded retry Accept/перебор candidates, rate-limit/backoff handshake — H3/H5);
   bind signaling/ICE к Wi-Fi/Ethernet интерфейсу (LOW-9).
2. **Field matrix**: входящий звонок, audio focus loss/regain, Bluetooth/наушники/route change,
   конкурентный микрофон, screen-off >1 ч, уничтожение Activity при живом service, Doze/App Standby,
   battery saver, OEM-агрессивная экономия (Huawei/Honor, Pixel, Samsung, Xiaomi/Redmi; минимум
   Android 12/13/14+).
3. **Soak/performance**: 8-часовой soak без роста памяти/latency; 100 connect/disconnect/reconnect
   циклов; battery drain/CPU/temperature/allocations на слабом устройстве; профиль Opus/VOICE_COMMUNICATION.
4. **Accessibility/UX robustness**: TalkBack-проход основных экранов, contentDescription, touch
   targets, ошибки/состояния читаемы; camera↔camera pairing на реальных устройствах.
5. **Release**: v0.4.0 по итогам 007 (или patch-релизы при исправлениях), обновление ADR/CHANGELOG/
   threats residual risks; подготовка данных для 008.

## Requirements

- **FR-001**: В сессии Child↔Parent должен быть явный control heartbeat: периодический ping (интервал
  и порог — измеряемая настройка), pong обновляет `SessionHealthMonitor`; отсутствие heartbeat
  дольше порога → typed `realtime.heartbeat_missed`/`Degraded`, независимо от media stall.
- **FR-002**: Parent обязан подавать локальную тревогу (ParentLossAlarm) при потере control heartbeat
  или media flow дольше настроенного порога; тревога не зависит от ответа Child, не блокирует UI,
  не содержит аудио/секретов; поведение покрыто unit-тестами с fake TimeProvider.
- **FR-003**: Telemetry counters/агрегаты по FR-022 005 (без аудио, SDP, fingerprint, IP): доступны
  диагностике и soak-тестам; частые события не логируются per-frame.
- **FR-004**: LAN availability: одиночный TCP connect+close/rogue mDNS не должен терминально ломать
  старт сеанса (bounded retry/перебор candidates); rate-limit/backoff неудачных handshake.
- **FR-005**: Signaling/ICE сокеты и candidates ограничиваются Wi-Fi/Ethernet интерфейсом; не
  слушаем не-LAN интерфейсы без явной необходимости.
- **FR-006**: Входящий звонок на Parent: audio focus loss останавливает воспроизведение; после
  звонка — восстановление или typed ошибка/reconnect по документированному поведению.
- **FR-007**: Bluetooth/наушники/route change на Parent: звук следует системному маршруту; смена
  маршрута не ломает сессию (или typed ошибка с восстановлением).
- **FR-008**: Экран выключен и Activity уничтожена: foreground service продолжает передачу; Stop из
  уведомления освобождает все ресурсы.
- **FR-009**: OEM-матрица: Huawei/Honor, Pixel, Samsung, Xiaomi/Redmi (минимум по одному устройству,
  разные API); поведение при агрессивной экономии батареи документируется, корректность не строится
  только на whitelist-инструкции.
- **FR-010**: 8-часовой soak: отсутствие роста memory/latency, отсутствие зависших session/audio
  ресурсов; 100 циклов reconnect без утечек.
- **FR-011**: Battery/CPU/temperature/allocations профилируются на слабом устройстве; ориентировочный
  drain за 30–60 минут фиксируется в docs/operations.
- **FR-012**: TalkBack: ключевые экраны (роль, pairing, мониторинг, диагностика) проходимы, статусы
  и ошибки читаемы; размеры touch targets >= 48dp.
- **FR-013**: Camera↔camera pairing на двух реальных устройствах (сканирование QR камерой) работает
  без сохранения/логирования кадров; отказ CAMERA — typed ошибка.
- **FR-014**: Все device-результаты (PASS/FAIL/PARTIAL/IOS) фиксируются в docs/operations с датой,
  моделями/API, сборкой/APK hash и краткими шагами; непроверенное не заявляется проверенным.

## Non-Functional

- **NFR-001**: Нет отключения TLS/pinning, cleartext signaling, записи/логов аудио (AGENTS §20).
- **NFR-002**: Архитектурные границы 005 сохраняются: Application/Domain без Android/SIPSorcery;
  platform только Platform.Android; audio adapters только Platform.Android/Audio.
- **NFR-003**: Все новые циклы/таймеры — с CancellationToken, TimeProvider, владельцем жизненного
  цикла; без fire-and-forget без наблюдения.
- **NFR-004**: Warnings-as-errors, format clean, architecture tests обновлены (heartbeat counters
  не протаскивают Android в Application).
- **NFR-005**: Battery/soak проверки не выполняются на зарядке; USB-adb допустим для снятия метрик.

## Acceptance Criteria

- [ ] AC-001: Control heartbeat + ParentLossAlarm реализованы и покрыты unit-тестами; на devices
  Parent сигнализирует потерю при выключенном Child/сети.
- [ ] AC-002: Telemetry counters доступны диагностике/soak без аудио и секретов.
- [ ] AC-003: LAN availability: одиночный connect+close/rogue mDNS не приводит к терминальному
  провалу старта в пределах настроенных повторов; handshake rate-limit.
- [ ] AC-004: Входящий звонок, Bluetooth/наушники, route change проверены на 2+ устройствах;
  результаты PASS/FAIL/PARTIAL зафиксированы.
- [ ] AC-005: Экран выключен/Activity уничтожена: сессия живёт; Stop очищает ресурсы (расширенная
  проверка >1 ч).
- [ ] AC-006: 8-часовой soak и 100 reconnect-циклов пройдены без утечек/деградации latency.
- [ ] AC-007: Battery/CPU/temperature/allocations измерены на слабом устройстве без зарядки;
  ориентировочный drain за 30–60 мин зафиксирован.
- [ ] AC-008: TalkBack-проход ключевых экранов выполнен; замечания закрыты.
- [ ] AC-009: Camera↔camera pairing на двух реальных устройствах выполнен.
- [ ] AC-010: OEM-матрица (минимум Huawei/Honor + Pixel + Samsung/Xiaomi) проверена или честно
  отмечена BLOCKED с причиной (нет устройств) — без заявления полной матрицы.
- [x] AC-011 (частично): Huawei nova Y90 + nova 13 Pro (Android 12): pairing, слышимость (owner
  confirmation), MediaFlowing, reconnect Wi-Fi off/on, screen-off 12 c, Stop — PASS
  (2026-11-08, v0.3.1).
- [ ] AC-012: Результаты 007 отражены в roadmap/CHANGELOG/threat model residual risks; релиз(ы)
  v0.4.x выпущены по правилам предохранителей.

## Вне scope

Видео, двусторонняя связь, облако/STUN/TURN, ML/детектор плача, аккаунты/реклама, Play-публикация
(решение владельца, 008), автоматические обновления (ADR-0008 — side-load).

## Ссылки

- [Plan](plan.md) · [Tasks](tasks.md) · [Requirements checklist](checklists/requirements.md)
- [005 spec](../005-session-reliability/spec.md) · [ADR-0007](../../../docs/architecture/adr/0007-local-security-signaling-pairing.md)
- [Threat model](../../../docs/security/pairing-threat-model.md) · [Device results 2026-11-08](../../../docs/operations/device-test-results-2026-11-08.md)
