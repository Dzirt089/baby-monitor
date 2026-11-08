namespace BabyMonitor.Application.Media;

/// <summary>
/// Композиция одного media-сеанса: координатор и его зависимости, которыми владеет composition root
/// и которые должны быть освобождены вместе с сеансом. Presentation/FGS работают через
/// <see cref="Coordinator"/>; освобождение — только через <see cref="DisposeAsync"/>.
/// </summary>
public sealed class MonitoringMediaSession : IAsyncDisposable
{
    private readonly IDisposable? _ownedCodec;
    private readonly IAsyncDisposable? _ownedDependency;
    private int _disposed;

    /// <summary>Создаёт обёртку сеанса.</summary>
    /// <param name="coordinator">Координатор media-сеанса (владеет transport/listener/audio/discovery).</param>
    /// <param name="ownedCodec">
    /// Кодек сеанса (IDisposable), созданный composition root-ом; освобождается этим объектом.
    /// Может быть null.
    /// </param>
    /// <param name="ownedDependency">
    /// Дополнительная зависимость, которой владеет composition root (например, IConnectivityMonitor);
    /// освобождается этим объектом. Может быть null.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="coordinator"/> равен null.</exception>
    public MonitoringMediaSession(
        MonitoringMediaCoordinator coordinator,
        IDisposable? ownedCodec,
        IAsyncDisposable? ownedDependency)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        Coordinator = coordinator;
        _ownedCodec = ownedCodec;
        _ownedDependency = ownedDependency;
    }

    /// <summary>Координатор media-сеанса.</summary>
    public MonitoringMediaCoordinator Coordinator { get; }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        // Порядок: координатор (циклы/сессии) → кодек → внешняя зависимость; сбой любого шага
        // не должен блокировать освобождение остальных (Application без logging).
        try
        {
            await Coordinator.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _ownedCodec?.Dispose();
            }
            catch (Exception)
            {
                // Best effort: см. XML-doc.
            }

            if (_ownedDependency is not null)
            {
                try
                {
                    await _ownedDependency.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Освобождение зависимости не должно подменять исход сеанса (Application без logging).
                }
            }
        }
    }
}
