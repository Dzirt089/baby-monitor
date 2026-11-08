using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Diagnostics;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.ForegroundServices;
using BabyMonitor.Platform.Android.Permissions;
using BabyMonitor.Platform.Android.Security;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Composition root экрана диагностики (per-Activity scope): foreground-хост для чтения snapshot,
/// trusted store и Application-модель диагностики. Activity не собирает зависимости сама.
/// </summary>
internal static class DiagnosticsComposition
{
    /// <summary>Создаёт host foreground-сессии и модель диагностики для роли.</summary>
    /// <param name="activity">Activity (permission host + visibility).</param>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    public static (AndroidForegroundSessionHost Host, DiagnosticsScreenModel Model) Create(
        PermissionActivity activity,
        DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var host = new AndroidForegroundSessionHost(
            activity,
            new AndroidNotificationPermission(activity, activity),
            new AndroidMicrophonePermission(activity, activity),
            new AndroidLocalNetworkPermission(activity, activity),
            activity);
        var store = new AndroidTrustedDeviceStore(activity);
        return (host, new DiagnosticsScreenModel(host, store, role));
    }
}
