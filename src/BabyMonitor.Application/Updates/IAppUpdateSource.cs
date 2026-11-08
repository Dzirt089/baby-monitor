using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Порт источника обновлений: получает манифест последнего опубликованного релиза.
/// Реализация — Infrastructure (HTTPS GitHub Releases); Application не видит HTTP/JSON.
/// </summary>
public interface IAppUpdateSource
{
    /// <summary>
    /// Запрашивает манифест последнего релиза. Ожидаемые отказы (сеть, HTTP, разбор JSON)
    /// возвращаются типизированной ошибкой, а не исключением.
    /// </summary>
    /// <param name="cancellationToken">Отмена запроса.</param>
    /// <returns>Манифест либо типизированная ошибка (без секретов).</returns>
    Task<Result<AppUpdateManifest>> GetLatestManifestAsync(CancellationToken cancellationToken);
}
