using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Порт публикации детского устройства в локальной Wi-Fi сети (NSD/DNS-SD).
/// Реализация — Platform.Android; Application не знает про Android/NSD-типы.
/// </summary>
public interface IPeerAdvertiser
{
    /// <summary>
    /// Публикует сервис Child. Ожидаемые отказы возвращаются типизированной ошибкой.
    /// </summary>
    /// <param name="advertisement">Данные advertisement (только version и role).</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Активная сессия либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<Result<IPeerAdvertiserSession>> StartAsync(PeerAdvertisement advertisement, CancellationToken cancellationToken);
}
