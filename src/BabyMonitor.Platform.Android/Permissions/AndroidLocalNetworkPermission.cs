using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Проверка/запрос ACCESS_LOCAL_NETWORK. На API ниже 36 разрешение не требуется; на API 36+
/// (opt-in local network permission) проверяется и при необходимости запрашивается через Activity.
/// </summary>
public sealed class AndroidLocalNetworkPermission : ILocalNetworkPermission
{
    /// <summary>Полное имя runtime-разрешения на локальную сеть.</summary>
    public const string AccessLocalNetworkPermission = "android.permission.ACCESS_LOCAL_NETWORK";

    private const int LocalNetworkPermissionApiLevel = 36;

    private readonly Context _context;
    private readonly IPermissionRequestHost _requestHost;

    /// <summary>Создаёт адаптер разрешения на локальную сеть.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="requestHost">Хост запроса runtime-разрешений (Activity/UI).</param>
    public AndroidLocalNetworkPermission(Context context, IPermissionRequestHost requestHost)
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

        if (OperatingSystem.IsAndroidVersionAtLeast(LocalNetworkPermissionApiLevel) is false)
        {
            // До Android 16 local network access не требует runtime permission.
            return Result<bool>.Success(true);
        }

        if (_context.CheckSelfPermission(AccessLocalNetworkPermission) == Permission.Granted)
        {
            return Result<bool>.Success(true);
        }

        var granted = await _requestHost.RequestPermissionsAsync([AccessLocalNetworkPermission], cancellationToken);
        return granted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(DiscoveryErrorCodes.PermissionDenied, "Нет разрешения на доступ к локальной сети."));
    }
}
