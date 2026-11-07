namespace BabyMonitor.Platform.Android.Qr;

/// <summary>
/// Стабильные машинно-читаемые коды ошибок сканирования QR-кода. Коды не содержат payload,
/// секретов или персональных данных и используются UI/ViewModel для typed-ошибок.
/// </summary>
public static class QrScannerErrorCodes
{
    /// <summary>Пользователь не предоставил runtime-разрешение CAMERA.</summary>
    public const string CameraPermissionDenied = "qr.camera_permission_denied";

    /// <summary>На устройстве нет доступной камеры (или CameraManager недоступен).</summary>
    public const string CameraUnavailable = "qr.camera_unavailable";

    /// <summary>Камеру не удалось открыть или сконфигурировать capture session.</summary>
    public const string CameraOpenFailed = "qr.camera_open_failed";

    /// <summary>Preview surface TextureView ещё не готова, уничтожена или недоступна.</summary>
    public const string PreviewUnavailable = "qr.preview_unavailable";
}
