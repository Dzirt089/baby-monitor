using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Активная advertise-сессия: публикация Child в mDNS. Реализуется platform-адаптером (NSD).
/// </summary>
/// <remarks>
/// Переходы состояний: Starting → Advertising → Failed | Stopped; Failed → Stopped;
/// Stopped — терминальное состояние. Starting является внутренним: StartAsync возвращает сессию
/// уже в Advertising. TXT содержит только protocol version и роль.
/// DisposeAsync эквивалентен StopAsync: снимает сервис из mDNS, освобождает NSD/multicast-ресурсы;
/// повторный вызов безопасен.
/// </remarks>
public interface IPeerAdvertiserSession : IAsyncDisposable
{
    /// <summary>Текущее состояние advertise.</summary>
    PeerAdvertisementState State { get; }

    /// <summary>Последняя типизированная ошибка advertise, если сессия завершилась ошибкой.</summary>
    Error? LastError { get; }

    /// <summary>Уведомление об изменении состояния; начальное состояние читается из <see cref="State"/>.</summary>
    event EventHandler<PeerAdvertisementStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Снимает сервис из mDNS и освобождает NSD/multicast-ресурсы. Идемпотентно; после возврата
    /// состояние Stopped. Отмена не отменяет освобождение ресурсов.
    /// </summary>
    /// <param name="cancellationToken">Ограничение ожидания остановки.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
