using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Порт проверки/запроса разрешения на доступ к локальной сети. Скрывает различия Android API
/// levels (на старых версиях разрешение не требуется, на новых — runtime permission).
/// </summary>
public interface ILocalNetworkPermission
{
    /// <summary>
    /// Проверяет и при необходимости запрашивает разрешение на локальную сеть.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания ответа пользователя.</param>
    /// <returns>
    /// Успех (true) — доступ разрешён или не требуется на этой версии ОС; отказ —
    /// типизированная ошибка <see cref="DiscoveryErrorCodes.PermissionDenied"/>.
    /// </returns>
    Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken);
}
