namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Итог/этап установки APK из PackageInstaller-сессии. Не содержит PII: только состояние,
/// которое UI показывает как «подтверждена/отменена/ошибка».
/// </summary>
public enum UpdateInstallStatus
{
    /// <summary>Статус неизвестен (повреждённое persisted-значение).</summary>
    Unknown = 0,

    /// <summary>PackageInstaller ждёт подтверждения пользователя в системном диалоге.</summary>
    PendingUserAction = 1,

    /// <summary>Установка подтверждена системой (SUCCESS).</summary>
    Succeeded = 2,

    /// <summary>Пользователь отменил установку (ABORTED).</summary>
    Cancelled = 3,

    /// <summary>Установка завершилась ошибкой.</summary>
    Failed = 4
}

/// <summary>Аргументы события изменения статуса установки APK.</summary>
public sealed class UpdateInstallStatusChangedEventArgs : EventArgs
{
    /// <summary>Создаёт аргументы события.</summary>
    /// <param name="status">Новый статус установки.</param>
    public UpdateInstallStatusChangedEventArgs(UpdateInstallStatus status) => Status = status;

    /// <summary>Новый статус установки.</summary>
    public UpdateInstallStatus Status { get; }
}
