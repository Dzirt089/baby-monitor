namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>Стабильные коды ошибок foreground-сессии мониторинга.</summary>
public static class ForegroundSessionErrorCodes
{
    /// <summary>Нет разрешения на уведомления (Android 13+), без него нельзя показать Stop.</summary>
    public const string NotificationPermissionDenied = "foreground.notification_permission_denied";

    /// <summary>Нет разрешения на микрофон (RECORD_AUDIO), требуется Child для FGS типа microphone.</summary>
    public const string MicrophonePermissionDenied = "foreground.microphone_permission_denied";

    /// <summary>Попытка запуска, когда Activity не видна (Android требует явное действие пользователя).</summary>
    public const string BackgroundStartDenied = "foreground.background_start_denied";

    /// <summary>Foreground service недоступен на устройстве или не запустился.</summary>
    public const string ServiceUnavailable = "foreground.service_unavailable";

    /// <summary>Некорректная роль (Unknown или несовместимая с мониторингом).</summary>
    public const string InvalidRole = "foreground.invalid_role";

    /// <summary>Уже есть активная foreground-сессия с другой ролью.</summary>
    public const string AlreadyRunning = "foreground.already_running";

    /// <summary>Нет активной foreground-сессии для остановки.</summary>
    public const string NotRunning = "foreground.not_running";
}
