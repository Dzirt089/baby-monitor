using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Активная точка ожидания доверенного peer (Child). После TLS/identity handshake возвращает
/// transport-сессию; недоверенный peer отклоняется внутри адаптера и не даёт сессию.
/// </summary>
public interface IRealtimeTransportListenerSession : IAsyncDisposable
{
    /// <summary>Фактический локальный порт signaling (0 → назначен ОС; публикуется через NSD).</summary>
    int BoundPort { get; }

    /// <summary>
    /// Ожидает одного доверенного peer и возвращает transport-сессию. Ожидаемый отказ реализации
    /// возвращается типизированной ошибкой, а не исключением; после успешного Accept повторный
    /// вызов возвращает invalid_state.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>Сессия либо типизированная ошибка (<see cref="RealtimeTransportErrorCodes"/>).</returns>
    /// <exception cref="OperationCanceledException">Ожидание отменено.</exception>
    Task<Result<IRealtimeTransportSession>> AcceptAsync(CancellationToken cancellationToken);
}
