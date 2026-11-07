using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Results;
using BabyMonitor.Platform.Android.Qr;

namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Проверка/запрос runtime-разрешения CAMERA для сканирования QR-кода. Сканирование доступно
/// только с явного действия пользователя; отказ возвращается типизированной ошибкой, а не
/// исключением. Parent-роль использует адаптер только для сканера QR, микрофон не затрагивается.
/// </summary>
public sealed class AndroidCameraPermission
{
    /// <summary>Полное имя runtime-разрешения на камеру.</summary>
    public const string CameraPermission = "android.permission.CAMERA";

    private readonly Context _context;
    private readonly IPermissionRequestHost _requestHost;

    /// <summary>Создаёт адаптер разрешения на камеру.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="requestHost">Хост запроса runtime-разрешений (Activity/UI).</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="context"/> или <paramref name="requestHost"/> равны <see langword="null"/>.
    /// </exception>
    public AndroidCameraPermission(Context context, IPermissionRequestHost requestHost)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestHost);
        _context = context;
        _requestHost = requestHost;
    }

    /// <summary>
    /// Гарантирует наличие CAMERA: уже выданное разрешение не запрашивается повторно, иначе
    /// показывается системный диалог через <see cref="IPermissionRequestHost"/>.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания ответа пользователя.</param>
    /// <returns>
    /// <see cref="Result{T}"/> с <see langword="true"/> при наличии разрешения либо типизированная
    /// ошибка <see cref="QrScannerErrorCodes.CameraPermissionDenied"/> при отказе.
    /// </returns>
    public async Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_context.CheckSelfPermission(CameraPermission) == Permission.Granted)
        {
            return Result<bool>.Success(true);
        }

        var granted = await _requestHost.RequestPermissionsAsync([CameraPermission], cancellationToken);
        return granted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                QrScannerErrorCodes.CameraPermissionDenied,
                "Нет разрешения на камеру: сканирование QR-кода недоступно."));
    }
}
