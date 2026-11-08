namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Мост live-статуса установки APK между <see cref="UpdateInstallResultReceiver"/> (создаётся
/// системой, не получает DI) и UI. Только транспорт enum-статуса без PII; источником истины между
/// процессами остаётся persisted-статус в <see cref="AndroidUpdateInstallStatusStore"/>.
/// </summary>
/// <remarks>
/// Статическое событие — вынужденная граница manifest-instantiated BroadcastReceiver: receiver
/// не может получить composition root. Подписчик (App composition) обязан отписываться в Dispose;
/// подписка защищена lock-ом, событие может подниматься с binder-потока.
/// </remarks>
public static class UpdateInstallStatusNotifier
{
    private static readonly object Sync = new();
    private static EventHandler<UpdateInstallStatusChangedEventArgs>? _statusChanged;

    /// <summary>Live-статус установки изменился (может подниматься с фонового потока).</summary>
    public static event EventHandler<UpdateInstallStatusChangedEventArgs>? StatusChanged
    {
        add
        {
            lock (Sync)
            {
                _statusChanged += value;
            }
        }

        remove
        {
            lock (Sync)
            {
                _statusChanged -= value;
            }
        }
    }

    /// <summary>Публикует live-статус установки (вызывает только receiver).</summary>
    /// <param name="status">Новый статус установки.</param>
    internal static void Publish(UpdateInstallStatus status)
    {
        EventHandler<UpdateInstallStatusChangedEventArgs>? handler;
        lock (Sync)
        {
            handler = _statusChanged;
        }

        handler?.Invoke(null, new UpdateInstallStatusChangedEventArgs(status));
    }
}
