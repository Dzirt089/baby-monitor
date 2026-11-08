using BabyMonitor.Application.Monitoring;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.ForegroundServices;
using BabyMonitor.Platform.Android.Permissions;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Composition root экрана мониторинга (per-Activity scope): создаёт Android-адаптеры и Application
/// модель; Activity не собирает зависимости сама.
/// </summary>
internal static class MonitoringComposition
{
    public static (AndroidForegroundSessionHost Host, MonitoringScreenModel Model) Create(
        Presentation.PermissionActivity activity,
        DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(activity);
        var host = new AndroidForegroundSessionHost(
            activity,
            new AndroidNotificationPermission(activity, activity),
            new AndroidMicrophonePermission(activity, activity),
            new AndroidLocalNetworkPermission(activity, activity),
            activity);
        return (host, new MonitoringScreenModel(host, role));
    }
}
