# ADR-0005: Media-транспорт — SIPSorcery или thin binding к Android libwebrtc

- **Статус**: Proposed (требуется spike 001)
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор по результатам device spike
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

## Решение (предлагаемое к проверке)

До spike: не писать custom UDP/encryption/jitter buffer. Реализация скрывается за портом
IRealtimeTransport (Application), конкретная библиотека — деталь Infrastructure. Spike 001 обязан
сравнить кандидатов по критериям и оформить новый ADR (Accepted/Superseded) до production-кода.

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

## Проверка и критерии пересмотра

Spike 001: протокол измерений (устройства, API, сеть, длительность), результаты в ADR, решение
Accepted или Rejected. Пересмотр — при провале device matrix или критической уязвимости библиотеки.
