# Baby Monitor — руководство для AI-агента

> Единый источник обязательных правил для разработки приложения-радионяни.
> Файл предназначен для AI coding agents и разработчиков.
> Любое изменение должно сохранять безопасность, приватность, архитектурные границы и работоспособность длительной аудиосессии.

## 1. Приоритет правил

При конфликте требований применять следующий приоритет:

1. Прямое указание пользователя в текущей задаче.
2. Этот `AGENTS.md`.
3. Принятые решения и существующие контракты в коде.
4. Существующий стиль ближайшего корректного файла.
5. Общепринятые рекомендации .NET и Android.

Если запрос противоречит пунктам безопасности, приватности, направлению зависимостей или роли устройства — не обходить правило молча. Кратко описать конфликт, предложить безопасный вариант и задать один конкретный вопрос.

## 2. Назначение продукта

Приложение соединяет два Android-телефона в одной локальной Wi-Fi сети и передаёт звук от детского устройства к родительскому без обязательного облачного backend.

Роли строго асимметричны:

- **Child / Ребёнок**: работает микрофон; локальное воспроизведение и передача голоса родителя отсутствуют.
- **Parent / Родитель**: работает динамик или выбранное устройство вывода; микрофон не запрашивается, не открывается и не публикуется.

Основная ценность продукта — надёжно услышать ребёнка и своевременно узнать о потере связи. Низкая задержка важна, но достоверная индикация состояния и восстановление соединения важнее минимальной задержки любой ценой.

## 3. Границы MVP

В MVP входят:

- Один APK для обеих ролей.
- Явный выбор роли пользователем.
- Обнаружение детского устройства в текущей Wi-Fi сети.
- Безопасное первичное сопряжение двух устройств.
- Односторонняя передача audio-only Child → Parent.
- Фоновая работа через Android foreground service.
- Постоянное уведомление с ролью, состоянием и действием «Остановить».
- Автоматическое переподключение после кратковременного разрыва сети.
- Локальная тревога на Parent при потере control channel или audio flow.
- Диагностика состояния без записи аудиоданных.
- Работа при выключенном экране.

Не входят без отдельного решения пользователя:

- Видео.
- Доступ через интернет, облачный relay, STUN/TURN вне LAN-сценария.
- Запись, хранение или отправка аудио.
- Двусторонняя связь.
- Детектор плача, ML или распознавание речи.
- Несколько Child или несколько Parent в одной активной сессии.
- Регистрация аккаунтов, реклама, аналитические SDK.
- Микросервисы, внешний message broker или удалённая база данных.

Не расширять scope «заодно». Новая функция сначала получает явные use cases, риски, критерии приёмки и архитектурное решение.

## 4. Технологический базис

Базовый вариант:

- C# и актуальный LTS/поддерживаемый .NET SDK репозитория.
- Android target: `net10.0-android`, если иное не зафиксировано проектом.
- UI: .NET MAUI с единственным Android target либо .NET for Android, если MAUI ещё не принят.
- Presentation: MVVM без бизнес-логики во View и code-behind.
- Захват и воспроизведение: Android `AudioRecord` и `AudioTrack` через платформенные адаптеры.
- Обнаружение: Android NSD/DNS-SD/mDNS.
- Media: WebRTC audio-only, Opus, DTLS-SRTP.
- Первый C#-ориентированный media spike: SIPSorcery за `IRealtimeTransport`.
- Если spike не проходит device matrix, media adapter заменяется thin binding к Android libwebrtc; Domain и Application не меняются.
- DI: `Microsoft.Extensions.DependencyInjection`.
- Тесты: xUnit; test doubles преимущественно hand-written fakes.
- JSON: только `System.Text.Json`.
- Время: только инжектированный `TimeProvider`.

Новый NuGet/Gradle-пакет нельзя добавлять без проверки:

- зачем он нужен и почему недостаточно платформы/BCL;
- актуальности и сопровождения;
- лицензии и транзитивных лицензий;
- Android-совместимости и влияния на APK/AAB;
- security history;
- возможности скрыть пакет за внутренним портом.

AutoMapper, MediatR, FluentValidation, Mapster, service locator и контейнерные абстракции в Domain/Application не использовать без отдельного обоснованного решения.

## 5. Архитектура

Проект — модульный монолит с Clean Architecture и DDD-lite.

Направление зависимостей строгое:

```text
BabyMonitor.Domain             -> ничего, кроме BCL
BabyMonitor.Application        -> Domain
BabyMonitor.Infrastructure     -> Application, Domain
BabyMonitor.Platform.Android   -> Application, Infrastructure
BabyMonitor.App                -> Application, Infrastructure, Platform.Android
Tests                          -> тестируемые проекты
```

Ожидаемая структура:

```text
src/
├─ BabyMonitor.Domain/
│  ├─ Sessions/
│  ├─ Pairing/
│  ├─ Devices/
│  └─ SharedKernel/
├─ BabyMonitor.Application/
│  ├─ Abstractions/
│  ├─ Monitoring/
│  ├─ Pairing/
│  └─ Diagnostics/
├─ BabyMonitor.Infrastructure/
│  ├─ Realtime/
│  ├─ Discovery/
│  ├─ Security/
│  ├─ Persistence/
│  └─ Observability/
├─ BabyMonitor.Platform.Android/
│  ├─ Audio/
│  ├─ ForegroundServices/
│  ├─ Permissions/
│  ├─ Connectivity/
│  └─ Notifications/
└─ BabyMonitor.App/
   ├─ Views/
   ├─ ViewModels/
   ├─ Navigation/
   └─ Composition/
tests/
├─ BabyMonitor.Domain.Tests/
├─ BabyMonitor.Application.Tests/
├─ BabyMonitor.Infrastructure.Tests/
├─ BabyMonitor.Architecture.Tests/
└─ BabyMonitor.DeviceTests/
```

Если фактическая структура репозитория уже отличается, не создавать параллельную архитектуру. Сначала сопоставить существующие проекты с указанными обязанностями и предложить минимальную миграцию.

### Domain

Domain содержит модель и инварианты, но не Android/media plumbing.

Разрешено:

- Aggregates, Entities, Value Objects.
- Domain events.
- Политики состояния сессии.
- Типизированные причины отключения и ошибки предметной области.

Запрещено:

- MAUI, Android namespaces, сокеты, WebRTC/SIPSorcery, NSD.
- `ILogger`, файлы, БД, JSON, DI container.
- PCM/Opus buffers и сетевые пакеты как доменные сущности.
- Async API без реальной доменной необходимости.

### Application

Application реализует use cases и определяет порты. Он оркестрирует Domain, но не содержит Android/WebRTC-деталей.

Основные use cases:

- `StartChildMonitoring`.
- `StartParentMonitoring`.
- `DiscoverChildDevices`.
- `PairDevice`.
- `ConnectToChild`.
- `StopMonitoring`.
- `ForgetPairedDevice`.
- `GetSessionDiagnostics`.

Ключевые порты:

- `IAudioCapture`.
- `IAudioPlayback`.
- `IPeerDiscovery`.
- `IRealtimeTransport`.
- `IPairingService`.
- `ITrustedDeviceStore`.
- `IForegroundSessionHost`.
- `IConnectivityMonitor`.
- `ISessionTelemetry`.

### Infrastructure

Infrastructure реализует WebRTC, signaling, discovery, pairing, persistence и logging adapters. Конкретная библиотека не должна протекать в Application API.

### Platform.Android

Здесь находятся `AudioRecord`, `AudioTrack`, Android services, permissions, notifications, network callbacks, audio focus, routing, Keystore и версия-зависимые API.

### Presentation

ViewModel вызывает только Application use cases и наблюдает immutable snapshots. View и ViewModel не работают напрямую с `AudioRecord`, сокетами, WebRTC callbacks или Android `Service`.

Composition root и DI registrations находятся только в App/Composition.

## 6. Доменная модель

Использовать единый язык:

- `MonitoringSession` — сеанс наблюдения.
- `DeviceRole` — `Child` или `Parent`.
- `PairedDevice` — доверенное удалённое устройство.
- `MonitoringState` — состояние сессии.
- `SessionHealth` — состояние control/media/capture/playback.
- `DisconnectReason` — типизированная причина разрыва.

Главный aggregate — `MonitoringSession`.

Обязательные инварианты:

- Роли локального и удалённого устройства дополняют друг друга.
- Несопряжённое устройство не начинает monitoring session.
- Child создаёт только send-only audio.
- Parent создаёт только receive-only audio.
- Одновременно активна не более чем одна сессия.
- После `Stopped` старая сессия не возвращается в `Connected`.
- Потеря heartbeat и отсутствие media flow — разные состояния/причины.
- Ручной stop прекращает reconnect loop.

Ожидаемые состояния:

```text
Idle
-> Discovering | Advertising
-> Pairing
-> Connecting
-> Connected
-> MediaFlowing
-> Degraded
-> Reconnecting
-> MediaFlowing | Failed
-> Stopping
-> Idle
```

Не моделировать состояние набором несогласованных `bool`. Переходы выполняются одним конечным автоматом и тестируются таблицей допустимых переходов.

Value Objects по возможности делать immutable `readonly record struct` или sealed record/class, если требуется ссылочная семантика. `default(T)` нельзя использовать как «нет значения», если default является допустимым значением.

## 7. Аудиопоток

Pipeline:

```text
Child AudioRecord
-> bounded PCM buffer
-> optional level meter
-> Opus encoder
-> WebRTC/RTP transport
-> jitter buffer
-> Opus decoder
-> Parent AudioTrack
```

Правила:

- Audio loops никогда не выполняются на UI thread.
- Между стадиями только bounded buffers/channels.
- При перегрузке real-time pipeline старые кадры допускается сбрасывать; бесконечно накапливать latency запрещено.
- Не выделять новый большой массив для каждого audio frame; применять pool/owner semantics и гарантированный возврат буфера.
- Никакой записи PCM/Opus на диск.
- Никакого аудио в логах, telemetry или crash reports.
- Child не создаёт playback endpoint.
- Parent не создаёт capture endpoint и не запрашивает `RECORD_AUDIO`.
- Noise suppression и выбор audio source являются измеряемой настройкой, а не скрытым предположением.
- Audio focus и route changes обязательны для Parent.
- Capture silenced, playback underrun и media packets absent диагностируются раздельно.

Не писать собственный encryption, jitter buffer или production UDP protocol без отдельного ADR и доказательства необходимости. Лабораторный custom UDP transport допустим только как изолированный experimental adapter, не как production default.

## 8. Сеть и обнаружение

- Основной сценарий работает в одной Wi-Fi LAN без интернета.
- Child публикует `_babymonitor._tcp.local.` через NSD.
- Parent выполняет discover → resolve → connect.
- IP-адрес не является идентификатором и не сохраняется как постоянный endpoint.
- При смене сети/IP старый connect attempt отменяется, NSD resolve выполняется заново.
- mDNS TXT не содержит имени ребёнка, владельца, постоянного device ID, ключа или pairing secret.
- Ручной IP допустим только как диагностический fallback.
- Учитывать guest Wi-Fi, AP/client isolation, разные VLAN и запрет multicast.
- Для новых Android API доступ к local network скрывать за platform permission adapter.

Различать ошибки:

- Wi-Fi отсутствует.
- Сервис не найден.
- Resolve не выполнен.
- Endpoint недоступен.
- Pairing отклонён.
- Signaling поднят, media path отсутствует.
- Активный media stream потерян.

## 9. Фоновая работа Android

Мониторинг работает только через foreground service с постоянным notification.

- Child service объявляет тип `microphone`.
- Parent service объявляет тип `mediaPlayback`.
- Service запускается явным действием пользователя при видимой Activity.
- После reboot микрофон автоматически не запускается.
- Notification показывает роль, состояние соединения и действие Stop.
- Activity может быть уничтожена без остановки активного service.
- Service не хранит View/ViewModel/Activity references.
- Не получать manual wake lock без измеренной необходимости и ADR.
- OEM battery optimization проверяется на реальных устройствах; корректность нельзя строить только на whitelist-инструкции пользователю.

Manifest permissions должны быть минимальными и зависеть от роли/runtime path. Parent не должен получать microphone permission «на всякий случай».

## 10. Сопряжение и безопасность

Локальная Wi-Fi сеть считается недоверенной.

Минимальная модель:

1. Child создаёт одноразовый pairing token и QR.
2. QR содержит protocol version, одноразовый token и fingerprint публичной идентичности; private key в QR запрещён.
3. Устройства подтверждают владение ключами и показывают одинаковый короткий verification code.
4. Пользователь подтверждает код.
5. Сохраняются публичная идентичность peer и локальная защищённая идентичность.
6. Повторные подключения принимаются только от доверенного peer.

Обязательные правила:

- Долгосрочные private keys хранятся через Android Keystore.
- Media защищается DTLS-SRTP.
- Signaling аутентифицирован и привязан к paired device.
- Cleartext HTTP/WebSocket запрещён для pairing/signaling.
- Запрещены `DangerousAcceptAnyServerCertificateValidator` и глобальное отключение TLS validation.
- Не изобретать собственную криптографию.
- Tokens одноразовые, короткоживущие и защищены от replay.
- Секреты, fingerprints, tokens и private IP не попадают в production logs.
- Потерянное/забытое устройство можно удалить из trusted store.

Любое изменение pairing, key storage, TLS или transport считается security-sensitive: агент обязан описать threat, trust boundary, failure mode и тесты.

## 11. Надёжность

- Reconnect policy задаётся одним объектом конфигурации/политики.
- Использовать exponential backoff с jitter и верхним пределом.
- Retry loops всегда принимают `CancellationToken`.
- Heartbeat control channel и media health проверяются независимо.
- «WebRTC connected» не равно «пользователь слышит звук».
- Parent подаёт локальную тревогу после настроенного порога потери связи.
- Тревога не зависит от ответа Child.
- Network callback отменяет устаревшие операции.
- Каждая сессия и попытка соединения имеют correlation identifiers.
- После stop/dispose фоновые задачи, callbacks, channels и audio resources гарантированно завершаются.

Запрещены fire-and-forget tasks без владельца, наблюдения ошибок и управляемого lifetime.

## 12. Ошибки и Result

- Ожидаемые исходы use case возвращаются типизированным `Result`/discriminated outcome, а не исключением.
- Нарушение доменного инварианта — `DomainException` или более конкретный тип.
- Android/network/media exceptions перехватываются на границе адаптера, логируются безопасно и преобразуются в application error.
- Не бросать `Exception`, `ApplicationException` или общий `InvalidOperationException`, если возможен конкретный тип.
- Не скрывать ошибки пустым `catch`.
- Не использовать исключения для обычного ветвления состояния.
- Пользовательское сообщение отделять от технической диагностической причины.

## 13. C#-стиль

- Nullable reference types включены.
- Warnings as errors для собственного кода.
- File-scoped namespaces.
- `sealed` для классов, не предназначенных для наследования.
- Primary constructors только там, где они не ухудшают ясность и lifetime semantics.
- Приватные поля: `_camelCase`.
- `var` — когда тип очевиден справа; явный тип — когда он улучшает понимание.
- `ArgumentNullException.ThrowIfNull` и соответствующие guard APIs на публичных границах.
- Наружу возвращать immutable/read-only contracts, не изменяемый `List<T>`.
- Не раскрывать mutable внутренние коллекции и pooled buffers.
- Все async API получают `CancellationToken`, если операция может длиться или блокироваться.
- Library-код не использует `async void`, кроме строго необходимых UI event handlers.
- Запрещены `.Result`, `.Wait()` и sync-over-async.
- `ConfigureAwait(false)` применять последовательно только в library projects, если это принято проектом; не добавлять механически в MAUI UI flow.
- `DateTime.Now`/`UtcNow` напрямую запрещены; использовать `TimeProvider`.
- Магические значения заменять именованными options/policies/constants.
- Комментарии объясняют «почему», инвариант или workaround, а не пересказывают код.
- Публичные и нетривиальные внутренние контракты документировать XML-комментариями на русском.
- Имена типов и членов — английские; пользовательский текст и документация команды — русские.

## 14. Логирование и диагностика

- В Domain логов нет.
- Логи находятся на adapter/use-case boundaries.
- Использовать structured logging и source-generated `[LoggerMessage]` для hot paths.
- Не интерполировать секреты и audio payload.
- Correlation fields: `SessionId`, `ConnectionAttemptId`, обезличенный `PeerId`.
- Production logs не содержат pairing token, key material, полные fingerprints, имя ребёнка или аудиоданные.
- Частые audio callbacks не логируются на каждый frame.
- Для частых событий использовать counters/aggregates/rate limiting.
- Локальный диагностический журнал ограничен по размеру и экспортируется только явным действием пользователя.

Минимальные метрики:

- capture/playback underruns;
- packets sent/received/lost;
- RTT и jitter;
- media silence duration;
- buffer depth;
- encode/decode duration;
- reconnect count/duration;
- service lifetime;
- ориентировочный battery drain в soak tests.

## 15. Тестирование

Каждое изменение поведения сопровождается тестом. Исправление дефекта сначала получает regression test, воспроизводящий исходную проблему.

### Unit

- Все допустимые и запрещённые переходы `MonitoringSession`.
- Role invariants.
- Retry/backoff через fake `TimeProvider` и детерминированный jitter.
- Pairing expiration/replay.
- Mapping platform failures в `DisconnectReason`.
- ViewModel с fakes, без Android runtime.

### Contract

- Один набор contract tests для всех реализаций `IRealtimeTransport`.
- Signaling protocol compatibility.
- Сериализация обязательных полей.
- Opus encode/decode round-trip для согласованного профиля.

### Integration/device

- NSD advertise/discover/resolve на двух устройствах.
- Потеря Wi-Fi на разные интервалы.
- Смена IP после reconnect.
- Выключенный экран и длительный foreground run.
- Входящий звонок и audio focus loss.
- Подключение/отключение Bluetooth и наушников.
- Конкурирующий захват микрофона.
- Уничтожение Activity при живом service.
- Battery Saver/Doze/OEM restrictions.
- Guest network/AP isolation с понятной ошибкой.

Имена тестов описывают сценарий и ожидаемый результат. Допустим русский стиль:

```csharp
public void Родительская_роль_не_создаёт_аудиозахват()
public async Task Потеря_сети_запускает_ограниченное_переподключение()
```

Один тест проверяет одно поведение; несколько assertions допустимы, если описывают одно целостное состояние.

## 16. Quality gates

Перед завершением задачи агент обязан выполнить доступные проверки, релевантные изменению:

```bash
dotnet restore
dotnet build --no-restore
dotnet test --no-build
```

Если в репозитории есть format/analyzers/architecture tests, запускать и их. Если команда недоступна из-за Android SDK, emulator, сертификата или hardware, не утверждать, что проверка пройдена: явно указать, что запущено, что не запущено и почему.

Release readiness дополнительно требует:

- 8-часовой soak test без роста memory и latency.
- 100 connect/disconnect/reconnect циклов.
- Проверку минимум на Pixel, Samsung, Huawei/Honor и Xiaomi/Redmi разных API levels.
- Проверку Android 13+ notification permission и Android 14+ foreground restrictions.
- Проверку, что Parent не получает `RECORD_AUDIO`.
- Security/secret/dependency/license scan.
- Подтверждение отсутствия audio payload и secrets в логах.
- Профилирование CPU, allocations, температуры и battery drain на слабом устройстве.

## 17. Git и рабочая копия

- Не удалять и не перезаписывать пользовательские изменения вне задачи.
- Не выполнять destructive Git-команды (`reset --hard`, `clean -fd`, force push) без прямого разрешения.
- Не изменять lock/config/workflow файлы без необходимости задачи.
- Не форматировать весь репозиторий ради локального изменения.
- Не создавать commit, branch, tag или PR без явной просьбы.
- Не включать secrets, signing keys, keystore files и локальные machine paths.
- Изменения должны быть малыми, тематически цельными и пригодными для review.

При параллельной работе агентов:

- Не убивать процессы по имени.
- Завершать только процесс, запущенный текущим агентом, по сохранённому PID.
- Не запускать конкурирующие `build/test` в одной working tree, если они используют общие `bin/obj`.
- Не изменять файлы, явно закреплённые за другой задачей/агентом.
- Для emulator/device test фиксировать используемый device serial.

## 18. Порядок работы агента

Перед изменением:

1. Прочитать этот файл полностью.
2. Изучить solution, project references и ближайшие реализации/тесты.
3. Сформулировать затронутые use cases, слои и риски.
4. Проверить, не нарушает ли задача роли Child/Parent, privacy или dependency direction.
5. Для нетривиальной задачи дать короткий план.

Во время изменения:

1. Вносить минимально достаточное изменение.
2. Сначала менять Domain/Application contract, затем adapters и UI, если задача действительно пересекает слои.
3. Не протаскивать platform types через внутренние слои.
4. Добавлять cancellation, disposal, logging и tests одновременно с поведением.
5. Не оставлять `TODO`, заглушки, `NotImplementedException` и закомментированный код без явного согласования.

После изменения:

1. Просмотреть diff на scope creep, secrets и нарушение зависимостей.
2. Запустить релевантные тесты и build.
3. Сообщить изменённые файлы и поведение.
4. Перечислить реально выполненные проверки.
5. Указать риски и непроверенные hardware-сценарии.

## 19. Формат ответа агента

Для задачи с кодом ответ должен содержать:

1. Краткое понимание задачи и выбранный слой.
2. Перечень выполненных изменений.
3. Перечень добавленных/изменённых тестов.
4. Результаты команд проверки.
5. Ограничения, риски или то, что требует реального Android-устройства.

Не выводить полный текст каждого изменённого файла, если пользователь явно этого не запросил: для repository agent достаточно diff/изменений в рабочей копии. Если агент не может изменять файлы и только генерирует код, тогда указывать путь и давать полный компилируемый файл без `...`.

Если данных не хватает, задать один наиболее важный вопрос и остановиться. Не делать несколько несовместимых реализаций «на выбор» без необходимости.

## 20. Безусловно запрещено

- Менять направленность слоёв.
- Помещать Android/WebRTC types в Domain/Application contracts.
- Запрашивать/открывать микрофон в Parent role.
- Включать playback/двустороннюю связь в Child role без отдельной функции.
- Записывать, сохранять или логировать аудио.
- Использовать cleartext signaling или отключать проверку сертификата.
- Хранить private keys вне Android Keystore без security decision.
- Доверять peer только по IP/имени mDNS.
- Использовать unbounded buffers для real-time audio.
- Запускать бесконечные retry/fire-and-forget loops без cancellation и owner.
- `async void` вне UI events; `.Result`; `.Wait()`.
- Пустой `catch`, подавление ошибки без диагностики.
- `DateTime.Now`/`UtcNow` вместо `TimeProvider`.
- Service locator, глобальные mutable singletons, скрытые статические зависимости.
- Добавлять пакеты и SDK без проверки необходимости/лицензии.
- Объявлять hardware/device сценарий проверенным без фактического прогона.
- Расширять MVP без явного решения пользователя.

## 21. Definition of Done

Задача завершена, когда одновременно выполнено:

- Поведение соответствует use case и критериям приёмки.
- Архитектурные зависимости не нарушены.
- Инварианты ролей сохранены.
- Privacy/security impact рассмотрен.
- Cancellation, cleanup и ошибки обработаны.
- Добавлены релевантные automated tests.
- Build/tests реально выполнены либо честно указана причина невозможности.
- Нет новых warnings, secrets, TODO и мёртвого кода.
- Документация обновлена, если изменён публичный контракт, протокол или эксплуатация.
- Для Android/media изменений указано, что ещё должно быть проверено на реальном устройстве.

Этот файл является живым контрактом. Менять его следует отдельным осознанным изменением, если решение влияет на продукт, архитектуру, безопасность или общий процесс разработки.
