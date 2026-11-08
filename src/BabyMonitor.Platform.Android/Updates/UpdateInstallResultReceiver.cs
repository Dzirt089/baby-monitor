using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Runtime;
using Android.Util;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Receiver результата PackageInstaller-сессии (009 review BLOCKER): при STATUS_PENDING_USER_ACTION
/// ОБЯЗАН запустить системный Intent подтверждения из <see cref="Intent.ExtraIntent"/>, иначе
/// установка остаётся pending и диалог не появляется. Финальный статус сохраняется в
/// <see cref="AndroidUpdateInstallStatusStore"/> (переживает смерть процесса) и публикуется в
/// <see cref="UpdateInstallStatusNotifier"/>; без PII/секретов.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class UpdateInstallResultReceiver : BroadcastReceiver
{
    private const string LogTag = "BabyMonitor.Updates";

    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || intent is null || !intent.HasExtra(PackageInstaller.ExtraStatus))
        {
            return;
        }

        var statusCode = intent.GetIntExtra(PackageInstaller.ExtraStatus, (int)PackageInstallStatus.Failure);
        var status = MapStatus(statusCode);
        if (status is UpdateInstallStatus.PendingUserAction)
        {
            LaunchUserConfirmation(context, intent);
            return;
        }

        Publish(context, status);
    }

    /// <summary>
    /// Запускает системный диалог подтверждения установки. Если intent недоступен/не запускается —
    /// сохраняет Failed, чтобы UI не оставался в состоянии «ожидание подтверждения».
    /// </summary>
    private static void LaunchUserConfirmation(Context context, Intent intent)
    {
        var confirmation = GetConfirmationIntent(intent);
        if (confirmation is null)
        {
            Log.Warn(LogTag, "PackageInstaller confirmation intent missing.");
            Publish(context, UpdateInstallStatus.Failed);
            return;
        }

        try
        {
            confirmation.AddFlags(ActivityFlags.NewTask);
            context.StartActivity(confirmation);
            Publish(context, UpdateInstallStatus.PendingUserAction);
        }
        catch (Exception exception) when (exception is ActivityNotFoundException or Java.Lang.SecurityException)
        {
            Log.Warn(LogTag, "PackageInstaller confirmation launch failed: " + exception.GetType().Name);
            Publish(context, UpdateInstallStatus.Failed);
        }
    }

    private static void Publish(Context context, UpdateInstallStatus status)
    {
        new AndroidUpdateInstallStatusStore(context).Save(status);
        UpdateInstallStatusNotifier.Publish(status);
    }

    private static UpdateInstallStatus MapStatus(int statusCode) => statusCode switch
    {
        (int)PackageInstallStatus.Success => UpdateInstallStatus.Succeeded,
        (int)PackageInstallStatus.PendingUserAction => UpdateInstallStatus.PendingUserAction,
        (int)PackageInstallStatus.FailureAborted => UpdateInstallStatus.Cancelled,
        _ => UpdateInstallStatus.Failed
    };

    private static Intent? GetConfirmationIntent(Intent intent)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            return GetConfirmationIntentApi33(intent);
        }

#pragma warning disable CA1422 // GetParcelableExtra(string) устарел на API 33+; на API 24–32 доступен только он.
        var parcelable = intent.GetParcelableExtra(Intent.ExtraIntent);
#pragma warning restore CA1422
        return AsIntent(parcelable);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("android33.0")]
    private static Intent? GetConfirmationIntentApi33(Intent intent) =>
        AsIntent(intent.GetParcelableExtra(Intent.ExtraIntent, Java.Lang.Class.FromType(typeof(Intent))));

    /// <summary>
    /// Приводит parcelable к <see cref="Intent"/>: для JNI-объекта используется JavaCast (wrapper
    /// может быть Java.Lang.Object без managed-типа), иначе обычный cast.
    /// </summary>
    private static Intent? AsIntent(object? parcelable) => parcelable switch
    {
        null => null,
        Intent confirmation => confirmation,
        Java.Lang.Object javaObject => javaObject.JavaCast<Intent>(),
        _ => null
    };
}
