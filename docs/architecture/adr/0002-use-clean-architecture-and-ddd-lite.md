# ADR-0002: Clean Architecture и DDD-lite с модульным монолитом

- **Статус**: Accepted
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор
- **Связанные документы**: [AGENTS.md](../../../AGENTS.md) раздел 5, [architecture.md](../architecture.md)

## Контекст

Домен (сессия наблюдения, роли, сопряжение, надёжность) содержит важные инварианты и должен
тестироваться без Android-эмулятора, WebRTC и сети. При этом media-стек может быть заменён
(SIPSorcery ↔ libwebrtc) без изменения бизнес-логики. Нужны жёсткие границы, но без микросервисов
и тяжёлых фреймворков — проект некоммерческий и поддерживается малой командой.

## Решение

Модульный монолит из пяти проектов с однонаправленными зависимостями:

```text
Domain -> (только BCL)
Application -> Domain
Infrastructure -> Application, Domain
Platform.Android -> Application, Infrastructure
App -> Application, Infrastructure, Platform.Android
```

Domain содержит модель и инварианты, Application — use cases и порты (IAudioCapture,
IRealtimeTransport, IPeerDiscovery, IPairingService, ITrustedDeviceStore, IForegroundSessionHost,
IConnectivityMonitor, ISessionTelemetry и др.), Infrastructure — адаптеры (WebRTC, signaling,
discovery, pairing, persistence, logging), Platform.Android — AudioRecord/AudioTrack, services,
permissions, notifications, network callbacks, audio focus/routing, Keystore; App — UI, MVVM,
composition root и DI. Строгий DDD (aggregate factories/domain services/repositories everywhere)
не применяется: DDD-lite с MonitoringSession как главным aggregate, value objects и domain events.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Почему отклонена |
|---|---|---|---|
| Один Android-проект | проще стартовать | домен смешан с платформой, тесты требуют Android | нарушает тестируемость инвариантов |
| Clean Architecture + MediatR/CQRS | модно, «чисто» | лишняя инфраструктура, сложность, новые пакеты | AGENTS.md требует избегать MediatR без необходимости |
| Микросервисы/отдельный backend | независимое масштабирование | не нужен в LAN-сценарии, повышает attack surface | вне MVP; основной сценарий без интернета |

## Последствия

- Домен и application тесты быстрые, без Android runtime.
- Media-библиотека скрыта за портом IRealtimeTransport.
- Больше проектов и церемоний: изменение use case затрагивает несколько слоёв.
- Направление зависимостей проверяется Architecture.Tests; нарушение блокирует merge.

## Проверка и критерии пересмотра

Architecture tests падают при изменении направления зависимостей или проникновении Android/
MAUI/WebRTC типов в Domain/Application. Пересмотр — только новым ADR при доказанной невозможности
поддерживать границы (например, требование единого AOT-модуля).
