using Android.Content;
using BabyMonitor.App.Presentation;
using BabyMonitor.Platform.Android.Updates;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Adapter Platform→App для итога установки APK: подписка на live-статус receiver-а и одноразовое
/// чтение persisted-статуса. Прокидывает только enum-итог, без PII и секретов.
/// </summary>
internal sealed class UpdateInstallStatusSource : IUpdateInstallStatusSource, IDisposable
{
    private readonly AndroidUpdateInstallStatusStore _store;
    private readonly EventHandler<UpdateInstallStatusChangedEventArgs> _platformHandler;
    private int _disposed;

    /// <summary>Создаёт адаптер статуса установки.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public UpdateInstallStatusSource(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _store = new AndroidUpdateInstallStatusStore(context);
        _platformHandler = OnPlatformStatusChanged;
        UpdateInstallStatusNotifier.StatusChanged += _platformHandler;
    }

    /// <inheritdoc />
    public event EventHandler<UpdateInstallOutcomeEventArgs>? OutcomeChanged;

    /// <inheritdoc />
    public UpdateInstallOutcome? TakeLastOutcome()
    {
        var snapshot = _store.TakeLastStatus();
        return snapshot is null ? null : MapOutcome(snapshot.Value.Status);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        UpdateInstallStatusNotifier.StatusChanged -= _platformHandler;
    }

    private void OnPlatformStatusChanged(object? sender, UpdateInstallStatusChangedEventArgs args)
    {
        var outcome = MapOutcome(args.Status);
        if (outcome is null)
        {
            return;
        }

        var handler = OutcomeChanged;
        if (handler is null)
        {
            // Экран обновлений закрыт: persisted-итог заберут при следующем открытии.
            return;
        }

        _store.Clear();
        handler.Invoke(this, new UpdateInstallOutcomeEventArgs(outcome.Value));
    }

    private static UpdateInstallOutcome? MapOutcome(UpdateInstallStatus status) => status switch
    {
        UpdateInstallStatus.Succeeded => UpdateInstallOutcome.Succeeded,
        UpdateInstallStatus.Cancelled => UpdateInstallOutcome.Cancelled,
        UpdateInstallStatus.Failed => UpdateInstallOutcome.Failed,
        _ => null
    };
}
