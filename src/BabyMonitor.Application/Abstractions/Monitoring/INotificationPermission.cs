using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>
/// Порт разрешения на уведомления. На Android 13+ POST_NOTIFICATIONS является runtime permission;
/// без него foreground service не сможет показать пользователю состояние и действие Stop.
/// </summary>
public interface INotificationPermission
{
    /// <summary>
    /// Проверяет и при необходимости запрашивает разрешение на уведомления.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания ответа пользователя.</param>
    /// <returns>
    /// Успех (true) — уведомления разрешены или не требуются на этой версии ОС; отказ —
    /// типизированная ошибка <see cref="ForegroundSessionErrorCodes.NotificationPermissionDenied"/>.
    /// </returns>
    Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken);
}
