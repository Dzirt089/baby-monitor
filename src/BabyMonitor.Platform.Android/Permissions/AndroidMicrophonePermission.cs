using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Permissions;

/// <summary>
/// Проверка/запрос RECORD_AUDIO. Запрашивается только для роли Child (prerequisite FGS-типа
/// microphone); Parent этот адаптер не вызывает и микрофон не получает. В 003 AudioRecord не
/// открывается — только permission plumbing для Android 14+ foreground restrictions.
/// </summary>
public sealed class AndroidMicrophonePermission : IMicrophonePermission
{
    /// <summary>Полное имя runtime-разрешения на микрофон.</summary>
    public const string RecordAudioPermission = "android.permission.RECORD_AUDIO";

    private readonly Context _context;
    private readonly IPermissionRequestHost _requestHost;

    /// <summary>Создаёт адаптер разрешения на микрофон.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="requestHost">Хост запроса runtime-разрешений (Activity/UI).</param>
    public AndroidMicrophonePermission(Context context, IPermissionRequestHost requestHost)
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

        if (_context.CheckSelfPermission(RecordAudioPermission) == Permission.Granted)
        {
            return Result<bool>.Success(true);
        }

        var granted = await _requestHost.RequestPermissionsAsync([RecordAudioPermission], cancellationToken);
        return granted
            ? Result<bool>.Success(true)
            : Result<bool>.Failure(new Error(
                ForegroundSessionErrorCodes.MicrophonePermissionDenied,
                "Нет разрешения на микрофон: детское устройство не сможет передавать звук."));
    }
}
