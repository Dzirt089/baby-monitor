using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Проверка/запрос POST_NOTIFICATIONS. На API ниже 33 разрешение не требуется; на 33+
/// проверяется и при необходимости запрашивается через Activity (permission host).
/// </summary>
public sealed class AndroidNotificationPermission : INotificationPermission
{
    /// <summary>Полное имя runtime-разрешения на уведомления.</summary>
    public const string PostNotificationsPermission = "android.permission.POST_NOTIFICATIONS";

    private const int NotificationPermissionApiLevel = 33;

    private readonly Context _context;
    private readonly IPermissionRequestHost _requestHost;

    /// <summary>Создаёт адаптер разрешения на уведомления.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="requestHost">Хост запроса runtime-разрешений (Activity/UI).</param>
    public AndroidNotificationPermission(Context context, IPermissionRequestHost requestHost)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestHost);
        _context = context;
        _requestHost = requestHost;
    }

    /// <inheritdoc />
    public async Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsAndroidVersionAtLeast(NotificationPermissionApiLevel) is false)
        {
            return Result<bool>.Success(true);
        }

        if (_context.CheckSelfPermission(PostNotificationsPermission) == Permission.Granted)
        {
            return Result<bool>.Success(true);
        }

        var granted = await _requestHost.RequestPermissionsAsync([PostNotificationsPermission], cancellationToken);
        return granted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                ForegroundSessionErrorCodes.NotificationPermissionDenied,
                "Нет разрешения на уведомления: невозможно показать состояние и кнопку «Остановить»."));
    }
}
