using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Проверка ACCESS_LOCAL_NETWORK без запроса UI (для foreground service / media-сеанса).
/// Запрос разрешения выполняет Activity (AndroidLocalNetworkPermission); service запускается только
/// после успешного старта из UI, поэтому здесь достаточно проверить, что разрешение уже выдано.
/// </summary>
public sealed class ContextLocalNetworkPermission : ILocalNetworkPermission
{
    private const int LocalNetworkPermissionApiLevel = 36;

    private readonly Context _context;

    /// <summary>Создаёт адаптер проверки.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен null.</exception>
    public ContextLocalNetworkPermission(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsAndroidVersionAtLeast(LocalNetworkPermissionApiLevel) is false)
        {
            return Task.FromResult(Result<bool>.Success(true));
        }

        var granted = _context.CheckSelfPermission(AndroidLocalNetworkPermission.AccessLocalNetworkPermission) == Permission.Granted;
        return Task.FromResult(granted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                DiscoveryErrorCodes.PermissionDenied,
                "Нет разрешения на доступ к локальной сети.")));
    }
}
