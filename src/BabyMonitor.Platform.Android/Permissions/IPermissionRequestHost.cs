namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Хост запроса runtime-разрешений: реализуется Activity/UI-слоем (Android требует Activity
/// для permission dialog). Позволяет адаптеру permissions не зависеть от конкретной Activity.
/// </summary>
public interface IPermissionRequestHost
{
    /// <summary>
    /// Запрашивает разрешения у пользователя и возвращает true, если все предоставлены.
    /// </summary>
    /// <param name="permissions">Полные имена Android permissions.</param>
    /// <param name="cancellationToken">Отмена ожидания ответа пользователя.</param>
    /// <returns>true — все разрешения предоставлены; false — отказ.</returns>
    Task<bool> RequestPermissionsAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken);
}
