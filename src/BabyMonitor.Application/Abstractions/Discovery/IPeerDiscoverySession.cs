using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Активная discovery-сессия: поиск Child в локальной Wi-Fi сети. Реализуется platform-адаптером
/// (NSD) и не содержит Android-типов в контракте.
/// </summary>
/// <remarks>
/// Переходы состояний: Starting → Discovering → Failed | Stopped; Failed → Stopped; Stopped —
/// терминальное состояние. Starting является внутренним: StartAsync возвращает сессию уже
/// в Discovering. Discovery не даёт доверия peer: identity проверяется pairing-слоем.
/// DisposeAsync эквивалентен StopAsync: останавливает discovery, освобождает NSD listeners,
/// multicast lock и фоновые задачи; повторный вызов безопасен.
/// </remarks>
public interface IPeerDiscoverySession : IAsyncDisposable
{
    /// <summary>Текущее состояние discovery.</summary>
    PeerDiscoveryState State { get; }

    /// <summary>Последняя типизированная ошибка discovery, если сессия завершилась ошибкой.</summary>
    Error? LastError { get; }

    /// <summary>Уведомление об изменении состояния; начальное состояние читается из <see cref="State"/>.</summary>
    event EventHandler<PeerDiscoveryStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Асинхронно перечисляет обнаруженных peer-ов. Перечисление завершается после остановки сессии;
    /// отмена приводит к <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="cancellationToken">Отмена перечисления.</param>
    /// <returns>Поток обнаруженных peer-ов.</returns>
    /// <exception cref="OperationCanceledException">Перечисление отменено.</exception>
    IAsyncEnumerable<DiscoveredPeer> DiscoverPeersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Останавливает discovery и освобождает NSD listeners, multicast lock и фоновые задачи.
    /// Идемпотентно; после возврата состояние Stopped. Отмена не отменяет освобождение ресурсов.
    /// </summary>
    /// <param name="cancellationToken">Ограничение ожидания остановки.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
