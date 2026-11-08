using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Util;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Receiver результата PackageInstaller-сессии: только безопасная диагностика (status без
/// сообщения/PII), UI не зависит от него — установку подтверждает пользователь в системном диалоге.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class UpdateInstallResultReceiver : BroadcastReceiver
{
    private const string LogTag = "BabyMonitor.Updates";

    /// <inheritdoc />
    public override void OnReceive(Context? context, Intent? intent)
    {
        var status = intent?.GetIntExtra(PackageInstaller.ExtraStatus, -1) ?? -1;
        if (status is (int)PackageInstallStatus.Success or (int)PackageInstallStatus.PendingUserAction)
        {
            // PendingUserAction — штатное ожидание подтверждения пользователя.
            return;
        }

        Log.Warn(LogTag, "APK install not completed: status=" + status);
    }
}
