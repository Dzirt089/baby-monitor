# ADR-0006: UI-фреймворк — MAUI или чистый .NET for Android

- **Статус**: Accepted (2026-11-07: выбран .NET for Android, Activity + XML; MAUI отклонён для MVP)
- **Дата**: 2026-10-20
- **Решение принимает**: ведущий архитектор (bootstrap-каркас уже .NET for Android; spike 006 подтвердил работоспособность в текущем CI)
- **Связанные документы**: [ADR-0003](0003-target-dotnet-android.md), [AGENTS.md](../../../AGENTS.md) разделы 4, 5

## Контекст

Приложению нужны 4–6 простых экранов: выбор роли, обнаружение/сопряжение ребёнка, состояние
сеанса, диагностика, настройки/доверенные устройства. Presentation — MVVM без бизнес-логики во
View/code-behind. Машина разработки не имеет установленного maui-android workload; bootstrap
собран на чистом .NET for Android (Activity + XML), чтобы проверить packaging APK.

AGENTS.md допускает оба варианта: MAUI с единственным Android target либо .NET for Android, если
MAUI ещё не принят. Решение не должно блокировать bootstrap-фазу.

## Решение (принято)

Для MVP выбран **.NET for Android (Activity + XML)**: bootstrap уже собран на нём, MAUI-workload
отсутствует на dev-машине и увеличивает CI/APK; для 4–6 простых экранов достаточно Activity+XML.
Presentation — MVVM: ViewModel вызывает только Application use cases/порты и наблюдает immutable
snapshots; Activity только связывает View и команды, без бизнес-логики. Composition root — только
в App/Composition (ручная сборка зависимостей на Activity-scope).

Критерии выбора:

- доступность workload на dev-машине и в CI (dotnet workload install в ubuntu-latest);
- влияние на размер APK/AAB и время сборки;
- зрелость MVVM-подхода, команд, навигации, доступности (a11y);
- возможность UI-тестов на эмуляторе/устройствах (инструменты, стабильность);
- жизнеспособность на API 24+ и OEM-устройствах;
- долгосрочная поддержка Microsoft и лицензии.

## Рассмотренные альтернативы

| Альтернатива | Плюсы | Минусы | Статус |
|---|---|---|---|
| .NET MAUI (single Android target) | XAML, MVVM, DI, единый стек | workload/размер APK, меньше контроля над Android UI | кандидат |
| .NET for Android (Views/XML) | лёгкий APK, полный доступ к Android UI | ручной binding, больше boilerplate MVVM | текущий каркас, кандидат |
| Kotlin/Compose UI | лучшая Android-поддержка | нет переиспользования C#, нарушает выбранный стек | отклонено |
| Blazor Hybrid/WebView | быстрый UI | web-стек в радионяне избыточен, offline/сложность | отклонено |

## Последствия

- BM-каркас не завязан на UI-фреймворк; Domain/Application от него не зависят.
- Финальный выбор фиксируется отдельным Accepted ADR и не переписывается задним числом.
- UI-задачи 006 начаты после фиксации этого Accepted-решения; решение не переписывается задним числом.

## Проверка и критерии пересмотра

Spike 006 выполнен как часть feature 006 phase 1: на .NET for Android реализованы экраны выбора роли
и мониторинга, запуск/остановка foreground service, emulator UI smoke. MAUI не проверялся (нет
workload), выбран прагматичный вариант с меньшим риском для CI. Пересмотр — если понадобится
сложная навигация/анимации, недоступные без MAUI.
