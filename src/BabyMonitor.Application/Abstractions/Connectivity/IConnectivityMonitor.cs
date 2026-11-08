using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Connectivity;

/// <summary>
/// Порт наблюдения за сетью: platform network callbacks (Android/Platform.Android). Используется
/// reconnect-loop-ом, чтобы отменять устаревшие connect/resolve попытки и перезапускать discovery
/// после смены сети/IP (AGENTS.md раздел 11). Событие может подниматься с фонового потока.
/// </summary>
public interface IConnectivityMonitor : IAsyncDisposable
{
    /// <summary>Сеть изменилась (потеря/восстановление/смена) — старые операции недействительны.</summary>
    event EventHandler? NetworkChanged;

    /// <summary>Возвращает текущий снимок сети; ошибка — typed (без секретов).</summary>
    /// <param name="cancellationToken">Отмена запроса.</param>
    /// <returns>Снимок либо типизированная ошибка.</returns>
    Task<Result<NetworkSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
}
