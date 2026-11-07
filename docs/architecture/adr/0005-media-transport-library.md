# ADR-0005: Media-транспорт — SIPSorcery или thin binding к Android libwebrtc

- **Статус**: Accepted (2026-11-07; решение владельца репозитория)
- **Дата**: 2026-10-20 (создан), 2026-11-07 (Accepted)
- **Решение принимает**: владелец репозитория Dzirt089 (принятие лицензии и выбор библиотеки подтверждены 2026-11-07)
- **Связанные документы**: [AGENTS.md](../../../AGENTS.md) разделы 4, 7; [roadmap](../../product/roadmap.md)

## Контекст

Нужен audio-only канал Child → Parent: Opus, DTLS-SRTP, jitter buffer, низкая задержка,
работа в LAN без интернета, стабильность на слабых устройствах. Кандидаты:

1. **SIPSorcery** — C#-ориентированная WebRTC-библиотека; первый C#-ориентированный spike
   зафиксирован в AGENTS.md.
2. **Thin binding к Android libwebrtc** — нативная проверенная реализация, но interop и
   сопровождение сложнее.

Решение нельзя принимать по предположению: нужны измерения на device matrix (Pixel, Samsung,
Huawei/Honor, Xiaomi/Redmi, разные API) и одинаковые contract tests за портом IRealtimeTransport.

## Решение (Accepted, 2026-11-07)

Выбрана **SIPSorcery** как media-транспорт MVP. Лицензия BSD-3-Clause + Additional Use Restriction
принята владельцем репозитория 2026-11-07; текст лицензии и upstream-ссылка зафиксированы в
[THIRD-PARTY-NOTICES.md](../../../THIRD-PARTY-NOTICES.md), краткая ссылка — в README.

Условия принятого решения:

- Версия пакета строго `>= 10.0.17` (в 10.0.14 исправлены три HIGH DoS-уязвимости; 10.0.17 —
  проверенная актуальная версия). Версия зафиксирована в `Directory.Packages.props`;
  `PackageReference` добавляется только вместе с реализацией media-адаптера (feature 003/005),
  не раньше.
- SIPSorcery-адаптер реализуется только в Infrastructure за портом `IRealtimeTransport`;
  типы SIPSorcery не протекают в Application/Domain. Адаптер обязан пройти общий набор
  `RealtimeTransportContractTests` (те же contract-тесты, что и fake).
- CI выполняет `dotnet list package --vulnerable --include-transitive`; при новых advisories —
  отдельная задача на обновление версии.
- License/SBOM-скан транзитивных зависимостей и измерение размера APK выполняются при добавлении
  пакета (phase 003/005).

До принятия решения: не писать custom UDP/encryption/jitter buffer. Реализация скрывается за портом
IRealtimeTransport (Application), конкретная библиотека — деталь Infrastructure.

Критерии выбора:

- стабильность соединения и reconnect, отсутствие утечек за 8 часов;
- end-to-end latency, CPU, allocations, battery drain на слабом устройстве;
- качество Opus-профиля (bitrate/FEC/PLC) и устойчивость к loss/jitter в LAN;
- поддержка DTLS-SRTP и совместимость с локальным signaling;
- размер APK/AAB, лицензии и зависимые лицензии, security history;
- сложность сопровождения и доступность документации/community;
- возможность прогонять contract tests в CI.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Статус |
|---|---|---|---|
| SIPSorcery | C#, порт IRealtimeTransport, проще тестировать | неизвестны метрики на device matrix | spike 001 |
| Thin binding libwebrtc | проверенный стек, низкая задержка | interop/сборка/размер, сложнее поддержка | fallback после spike |
| Custom UDP + Opus | полный контроль | собственная криптография/jitter — запрещено AGENTS.md | отклонено |
| Облачный WebRTC | не думать о NAT | требует интернет/облако, вне MVP, privacy | отклонено |

## Последствия

- До принятия ADR media-код в production не пишется; Infrastructure содержит только каркас.
- Contract tests IRealtimeTransport должны быть готовы до сравнения реализаций.
- Domain/Application не меняются при замене media-адаптера.

## Статус spike 001 (2026-10-20)

Phase 1 — выполнена:

- порт `IRealtimeTransport` и типы в Application (без Android/WebRTC/NSD-типов);
- общий contract suite `RealtimeTransportContractTests` + ручной test double,
  22 теста (15 общих contract + 7 fake-specific), шаг CI «Test Infrastructure (contract tests)»;
- тестовый проект `BabyMonitor.Infrastructure.Tests` добавлен в solution.

Phase 2 (оценка пакета SIPSorcery, версия 10.0.17 от 2026-10-02) — выполнена:

| Критерий | Факт (источники: NuGet API, GitHub API, OSV.dev, LICENSE.md) |
|---|---|
| Совместимость | пакет публикует TFM `net10.0` (а также netstandard2.0/2.1, net5–net9); в принципе совместим с net10.0-android, **Android/trimming/AOT не проверялись** — нужен отдельный эксперимент |
| Лицензия | BSD-3-Clause **плюс Additional Use Restriction** (запрет использования в Израиле/оккупированных территориях до выполнения требований BDS). SPDX = NOASSERTION, GitHub license = Other. **Не является стандартной OSI-лицензией; для публикации APK требуется явное юридическое решение владельца — пакет не добавлен** |
| Уязвимости | OSV: 3 HIGH DoS-уязвимости (CVE-2026-54632 и др.), все исправлены в 10.0.14+; при adoption обязательна версия ≥ 10.0.17 и мониторинг advisories. LAN считается недоверенной — DoS на RTP/ICE/TURN релевантен threat model |
| Транзитивные зависимости | SIPSorceryMedia.Abstractions 10.0.17, BouncyCastle.Cryptography 2.7.0, Concentus 2.2.2 (Opus), DnsClient 1.8.0, Makaretu.Dns.Multicast 0.27.0, SIPSorcery.WebSocketSharp 0.0.1, Microsoft.Extensions.Logging.Abstractions 10.0.11 — перед adoption нужен license/SBOM-скан |
| Сопровождение | активный upstream: ~1.9k stars, 34 open issues, push 2026-10-20, не archived |
| Размер APK/AAB | не измерялся; требует эксперимента с адаптером |

**Вывод phase 2**: технически SIPSorcery — жизнеспособный кандидат (C#, net10.0, Opus/DTLS-SRTP).
Лицензия нестандартная (BSD-3 + Additional Use Restriction, NOASSERTION); владелец принял её
2026-11-07 — см. раздел Accepted выше. Альтернатива (thin binding к libwebrtc, Apache-2.0) остаётся
fallback, если device matrix покажет неприемлемые метрики.

Phase 3 (device matrix) — **BLOCKED**: в окружении нет реальных Android-устройств. Emulator
(API 35) не воспроизводит акустический тракт, end-to-end latency, CPU/battery слабого устройства
и реальные сетевые условия; замеры на emulator не считаются доказательством.

### Протокол device matrix (для выполнения на реальных устройствах)

| Параметр | Требование |
|---|---|
| Устройства | минимум 2 реальных телефона разных производителей/API (например Pixel + Samsung/Xiaomi/Huawei) |
| Сеть | один Wi-Fi AP без гостевого режима/AP isolation; зафиксировать диапазон и модель AP |
| Сценарий | 30 минут непрерывной передачи; 10 кратких разрывов Wi-Fi; смена IP; выключенный экран |
| Метрики | e2e latency p50/p95, jitter, packet loss, CPU %, allocations, battery drain %/ч, reconnect count/duration, размер APK/AAB, memory leaks |
| Артефакты | commit SHA, device serial/API, CSV метрик, логи без аудио/секретов |
| Критерий выбора | отсутствие утечек/деградации; latency/CPU/battery в пределах, согласованных до старта |

## Проверка и критерии пересмотра

Решение Accepted; device matrix (phase 3) остаётся **BLOCKED** из-за отсутствия реальных устройств —
это проверка, а не условие принятия решения. При появлении устройств: протокол измерений (устройства,
API, сеть, длительность), результаты в ADR; при провале device matrix или критической уязвимости
библиотеки решение пересматривается новым ADR (fallback — thin binding libwebrtc).
