using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Порт поиска детского устройства в локальной Wi-Fi сети (NSD/DNS-SD). Реализация —
/// Platform.Android; Application не знает про Android/NSD-типы.
/// </summary>
public interface IPeerDiscovery
{
    /// <summary>
    /// Запускает discovery. Ожидаемые отказы (Wi-Fi выключен, нет разрешения, NSD недоступен)
    /// возвращаются как типизированная ошибка, а не исключение.
    /// </summary>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Активная сессия либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<Result<IPeerDiscoverySession>> StartAsync(CancellationToken cancellationToken);
}
