namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Активная transport-сессия с одним peer. Реализуется adapter-слоем (WebRTC-библиотекой);
/// не содержит platform-типов и не гарантирует, что media flow уже слышен пользователю.
/// </summary>
/// <remarks>
/// Переходы состояний: Connecting → Connected → Failed | Closed; Failed → Closed; Closed —
/// терминальное состояние. Событие <see cref="StateChanged"/> сообщает только о переходах после
/// создания сессии; начальное состояние читается из <see cref="State"/>.
/// Реализация обязана быть потокобезопасной для конкурентных Send/Receive/Close/Dispose.
/// </remarks>
public interface IRealtimeTransportSession : IAsyncDisposable
{
    /// <summary>Текущее состояние transport-соединения.</summary>
    RealtimeTransportState State { get; }

    /// <summary>Последняя типизированная транспортная ошибка, если соединение завершилось ошибкой.</summary>
    Results.Error? LastError { get; }

    /// <summary>
    /// Уведомление об изменении состояния; обработчики не должны блокировать поток транспорта.
    /// Событие сообщает только о последующих переходах: начальное состояние (например Connected
    /// после ConnectAsync) читается из <see cref="State"/>.
    /// </summary>
    event EventHandler<RealtimeTransportStateChangedEventArgs>? StateChanged;

    /// <summary>Уведомление о полученном control heartbeat (pong) от peer. Обработчики не должны блокировать поток транспорта.</summary>
    event EventHandler? HeartbeatReceived;

    /// <summary>
    /// Ставит закодированный аудиокадр в ограниченную очередь отправки.
    /// </summary>
    /// <remarks>
    /// Контракт: вызов не блокирует вызывающий поток на время сетевой передачи; очередь ограничена,
    /// при переполнении отбрасываются старые кадры (drop-oldest), latency не растёт безгранично.
    /// Адаптер обязан скопировать/арендовать данные кадра до возврата управления: вызывающий вправе
    /// переиспользовать буфер сразу после завершения вызова. Аудио не логируется, не сохраняется
    /// на диск и не передаётся куда-либо, кроме согласованного peer.
    /// </remarks>
    /// <param name="encodedAudioFrame">Закодированный аудиокадр (Opus), непустой.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">Передан пустой кадр.</exception>
    /// <exception cref="RealtimeTransportClosedException">Сессия уже закрыта или завершилась ошибкой.</exception>
    /// <exception cref="OperationCanceledException">Операция отменена до постановки кадра.</exception>
    ValueTask SendAudioAsync(ReadOnlyMemory<byte> encodedAudioFrame, CancellationToken cancellationToken);

    /// <summary>
    /// Асинхронно перечисляет принятые аудиокадры. Перечисление завершается после закрытия сессии;
    /// отмена токена приводит к <see cref="OperationCanceledException"/> и закрытию receive-канала.
    /// При ошибке транспорта перечисление завершается, а причина доступна через <see cref="LastError"/>
    /// и событие состояния Failed.
    /// </summary>
    /// <remarks>
    /// Receive-очередь ограничена и использует drop-oldest: при перегрузке старые кадры отбрасываются,
    /// поэтому latency воспроизведения не растёт безгранично. Аудио не логируется и не сохраняется.
    /// </remarks>
    /// <param name="cancellationToken">Отмена перечисления и закрытие receive-канала.</param>
    /// <returns>Поток закодированных кадров от peer.</returns>
    /// <exception cref="OperationCanceledException">Перечисление отменено.</exception>
    IAsyncEnumerable<ReceivedAudioFrame> ReceiveAudioAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Закрывает сессию; повторный вызов идемпотентен. После успешного завершения
    /// <see cref="State"/> равен Closed. Отмена токена не отменяет закрытие: она ограничивает
    /// только ожидание graceful shutdown, сессия не остаётся в промежуточном состоянии.
    /// </summary>
    /// <param name="cancellationToken">Ограничение ожидания корректного закрытия.</param>
    Task CloseAsync(CancellationToken cancellationToken);
}
