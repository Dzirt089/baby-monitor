# Feature Specification: 001-audio-transport-spike

**Feature Branch**: 001-audio-transport-spike
**Created**: 2026-10-20
**Status**: Done (2026-11-08). Phase 1 contract suite + ADR-0005 Accepted; SIPSorcery 10.0.17 adopted (лицензия подтверждена владельцем), media-адаптер реализован в 005 и device-verified на 2 Huawei (v0.3.1: MediaFlowing, owner-confirmed звук). Расширенная device matrix/метрики — 007.
**Input**: [ADR-0005](../../../docs/architecture/adr/0005-media-transport-library.md), [AGENTS.md](../../../AGENTS.md) разделы 4, 7, 15

## Контекст

До написания production-кода аудиотранспорта нужно выбрать media-библиотеку
(SIPSorcery как C#-ориентированный вариант против thin binding к Android libwebrtc) и зафиксировать
это решением ADR. Решение принимается только по фактическим измерениям на device matrix; сейчас
в распоряжении только emulator (Android 15/API 35), который не воспроизводит реальный акустический
тракт и слабые устройства. Поэтому feature разделена на фазы:

1. Контракт порта `IRealtimeTransport` + общий contract-test suite + CI-интеграция.
2. Оценка пакета-кандидата (лицензия, зависимости, security, размер, совместимость net10.0-android).
3. Device matrix (2 устройства в одной LAN; метрики latency/CPU/battery/стабильность).
4. ADR-0005: Accepted/Rejected с данными.

## User Scenarios

### US-1: Разработчик проверяет контракт транспорта (P1)

Разработчик добавляет новую реализацию `IRealtimeTransport` и запускает общий набор contract tests
без изменения Application/Domain. Suite проверяет успешное соединение, типизированный отказ,
отмену, закрытие/идемпотентность, поведение отправки после закрытия и события состояния.

**Acceptance**: реализация проходит suite; suite не зависит от библиотеки и платформы.

### US-2: Команда сравнивает кандидатов на реальных устройствах (P1)

Два Android-телефона в одной Wi-Fi сети выполняют согласованный сценарий; для каждого кандидата
фиксируются latency (end-to-end), CPU/allocations, battery drain, потери/reconnect, размер APK.
Результаты и methodology сохраняются в репозитории (без аудиозаписей и персональных данных).

**Acceptance**: минимум 2 устройства (разные производители/API), одинаковый сценарий и метрики
для каждого кандидата; данные приложены к ADR-0005.

### US-3: Принимается архитектурное решение (P2)

По результатам измерений ADR-0005 переводится в Accepted (выбранная библиотека) или остаётся
Proposed с Rejected-вариантами; production media-код не пишется до Accepted.

## Functional Requirements

- **FR-001**: Порт `IRealtimeTransport` и связанные типы (`TransportEndpoint`,
  `RealtimeTransportState`, `ReceivedAudioFrame`, события состояния) находятся в Application,
  не содержат Android/WebRTC/NSD-типов и не интерпретируют аудио.
- **FR-002**: Общий contract-test suite для любой реализации порта; deterministic, без реальной сети,
  выполняется в CI; включает успех/ошибку соединения, отмену, закрытие (идемпотентность),
  отправку после закрытия, события состояния и завершение receive-потока.
- **FR-003**: Spike protocol: устройства (производитель, модель, API level, serial), сеть (диапазон,
  AP), длительность, сценарий, пороги и метрики (latency p50/p95, jitter, CPU, allocations,
  battery drain, packets lost, reconnect count, размер APK/AAB).
- **FR-004**: Оценка пакета: лицензия и транзитивные лицензии, поддержка/релизы, security history,
  совместимость с net10.0-android/API 24+, влияние на размер APK, возможность скрыть за портом.
- **FR-005**: Кандидат-адаптер размещается только в Infrastructure за портом; он не становится
  default production-транспортом до ADR-0005 Accepted.
- **FR-006**: Bounded очереди и drop-oldest для real-time кадров; cancellation во всех операциях;
  никакой записи/логирования аудио; никаких platform-типов в Application.
- **FR-007**: Результаты и решение оформляются в ADR-0005 (Proposed → Accepted/Rejected) до
  production media-кода.

## Non-Functional Requirements

- **NFR-001**: Contract suite проходит в CI (ubuntu-latest) и не требует Android SDK/эмулятора.
- **NFR-002**: Application не получает новых пакетов; зависимость от media-библиотеки допускается
  только в Infrastructure после ADR-0005.
- **NFR-003**: Тесты не используют реальное аудио, сеть или timing-хрупкие задержки; асинхронные
  ожидания ограничены таймаутами.

## Acceptance Criteria

- [x] AC-001: Порт и типы реализованы в Application; сборка solution без warnings.
- [x] AC-002: Contract suite зелёный на ручном test double (22 теста: 15 общих contract + 7 fake-specific) и включён в CI.
- [x] AC-003: Architecture tests обновлены под 9-й проект (37 тестов, включая состав solution и references).
- [x] AC-004: Оценка SIPSorcery зафиксирована в ADR-0005: TFM net10.0, активный upstream, 3 HIGH DoS
  (fixed ≥10.0.14, использовать ≥10.0.17); лицензия BSD-3-Clause + Additional Use Restriction
  (NOASSERTION) **принята владельцем 2026-11-07**, пин ≥10.0.17 в Directory.Packages.props,
  THIRD-PARTY-NOTICES.md; PackageReference появится в 003/005.
- [ ] AC-005: Spike protocol задокументирован; device matrix выполнен на 2 реальных устройствах
  (сейчас **[BLOCKED]**: реальных устройств нет, emulator не валиден для аудиотракта).
- [x] AC-006: ADR-0005 переведён в Accepted (2026-11-07) решением владельца (лицензия принята);
  device matrix (AC-005) остаётся BLOCKED и выполняется на реальных устройствах позже.

## Out of Scope

Production audio pipeline (AudioRecord/AudioTrack/Opus encode/decode), реальный signaling/pairing,
NSD, foreground service, UI, автоподключение/reconnect-политика, запись/хранение аудио. Выбор
библиотеки не означает реализацию мониторинга.

## Dependencies & Assumptions

- 2 реальных Android-устройства в одной Wi-Fi LAN для phase 3 (пока не предоставлены).
- SIPSorcery принят (ADR-0005 Accepted 2026-11-07): pin >= 10.0.17 в Directory.Packages.props,
  THIRD-PARTY-NOTICES.md; PackageReference и адаптер добавляются в 003/005 только в Infrastructure.
- Emulator (API 35) допускается только для smoke компиляции/упаковки, не для audio-метрик.