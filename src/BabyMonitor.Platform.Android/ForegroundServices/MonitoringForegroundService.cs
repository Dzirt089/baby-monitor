using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.OS;
using Android.Util;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Platform.Android.ForegroundServices;

/// <summary>
/// Foreground service мониторинга: постоянное уведомление с ролью, состоянием и действием Stop.
/// Child объявляет тип microphone, Parent — mediaPlayback; в 003 аудио ещё не открывается
/// (media pipeline появится в 005). Service не хранит ссылок на Activity/View/ViewModel.
/// </summary>
[Service(
    Exported = false,
    ForegroundServiceType = global::Android.Content.PM.ForegroundService.TypeMicrophone
        | global::Android.Content.PM.ForegroundService.TypeMediaPlayback)]
public sealed class MonitoringForegroundService : Service
{
    /// <summary>Действие запуска foreground-сессии.</summary>
    public const string ActionStart = "BabyMonitor.ForegroundServices.action.START";

    /// <summary>Действие остановки foreground-сессии (из уведомления или host).</summary>
    public const string ActionStop = "BabyMonitor.ForegroundServices.action.STOP";

    /// <summary>Дополнительный параметр роли при запуске.</summary>
    public const string ExtraRole = "BabyMonitor.ForegroundServices.extra.ROLE";

    private const string LogTag = "BabyMonitor.Foreground";
    private const int NotificationId = 1001;
    private const int StopRequestCode = 1002;
    private const int OpenRequestCode = 1003;
    private const string ChannelId = "babymonitor_monitoring";
    private const string ChannelName = "Мониторинг";
    private const string ChannelDescription = "Постоянное уведомление о работе радионяни";
    private const string TitleChild = "Радионяня: ребёнок";
    private const string TitleParent = "Радионяня: родитель";
    private const string TextRunning = "Мониторинг активен";
    private const string TextStopped = "Мониторинг остановлен";
    private const string ActionStopLabel = "Остановить";

    private readonly object _sync = new();
    private readonly LocalBinder _binder;
    private ForegroundSessionSnapshot? _snapshot;

    /// <summary>Создаёт сервис; Android создаёт его без зависимостей приложения.</summary>
    public MonitoringForegroundService()
    {
        _binder = new LocalBinder(this);
    }

    /// <summary>Уведомление об изменении снимка; подписывается host через binder.</summary>
    public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

    /// <summary>Текущий снимок foreground-сессии.</summary>
    public ForegroundSessionSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot;
            }
        }
    }

    /// <inheritdoc />
    public override IBinder? OnBind(Intent? intent) => _binder;

    /// <inheritdoc />
    public override void OnCreate()
    {
        base.OnCreate();
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            CreateNotificationChannelApi26();
        }
    }

    /// <inheritdoc />
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action is null)
        {
            return StartCommandResult.NotSticky;
        }

        switch (intent.Action)
        {
            case ActionStart:
                HandleStart(intent);
                break;
            case ActionStop:
                StopSession();
                break;
        }

        return StartCommandResult.NotSticky;
    }

    /// <inheritdoc />
    public override void OnDestroy()
    {
        try
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "StopForeground failed: " + exception.GetType().Name);
        }

        PublishTerminalIfNeeded();
        base.OnDestroy();
    }

    private void HandleStart(Intent intent)
    {
        var roleText = intent.GetStringExtra(ExtraRole);
        if (!Enum.TryParse<DeviceRole>(roleText, ignoreCase: false, out var role)
            || role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            Log.Warn(LogTag, "Foreground service start rejected: invalid role.");
            PublishTerminalIfNeeded();
            StopSelf();
            return;
        }

        try
        {
            var notification = BuildNotification(role, MonitoringForegroundState.Running);
            if (OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                StartForegroundApi30(notification, role);
            }
            else if (OperatingSystem.IsAndroidVersionAtLeast(29) && role is DeviceRole.Parent)
            {
                StartForegroundApi29(notification, ForegroundService.TypeMediaPlayback);
            }
            else
            {
                StartForeground(NotificationId, notification);
            }

            Publish(new ForegroundSessionSnapshot(role, MonitoringForegroundState.Running));
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "StartForeground failed: " + exception.GetType().Name);
            Publish(new ForegroundSessionSnapshot(
                role,
                MonitoringForegroundState.Failed,
                new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Не удалось запустить foreground service.")));
            StopSelf();
        }
    }

    [SupportedOSPlatform("android30.0")]
    private void StartForegroundApi30(Notification notification, DeviceRole role)
    {
        var serviceType = role is DeviceRole.Child
            ? ForegroundService.TypeMicrophone
            : ForegroundService.TypeMediaPlayback;
        StartForeground(NotificationId, notification, serviceType);
    }

    [SupportedOSPlatform("android29.0")]
    private void StartForegroundApi29(Notification notification, ForegroundService serviceType)
    {
        StartForeground(NotificationId, notification, serviceType);
    }

    private void StopSession()
    {
        try
        {
            StopForeground(StopForegroundFlags.Remove);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "StopForeground failed: " + exception.GetType().Name);
        }

        PublishTerminalIfNeeded();
        StopSelf();
    }

    private void PublishTerminalIfNeeded()
    {
        ForegroundSessionSnapshot? current = Snapshot;
        if (current is null || current.IsTerminal)
        {
            return;
        }

        Publish(new ForegroundSessionSnapshot(current.Role, MonitoringForegroundState.Stopped));
    }

    private void Publish(ForegroundSessionSnapshot snapshot)
    {
        lock (_sync)
        {
            _snapshot = snapshot;
        }

        SnapshotChanged?.Invoke(this, snapshot);
    }

    [SupportedOSPlatform("android26.0")]
    private void CreateNotificationChannelApi26()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
        {
            return;
        }

        var channel = new NotificationChannel(ChannelId, ChannelName, NotificationImportance.Low)
        {
            Description = ChannelDescription
        };
        channel.SetShowBadge(false);
        manager.CreateNotificationChannel(channel);
    }

    private Notification BuildNotification(DeviceRole role, MonitoringForegroundState state)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return BuildNotificationApi26(role, state);
        }

        return BuildNotificationLegacy(role, state);
    }

    [SupportedOSPlatform("android26.0")]
    private Notification BuildNotificationApi26(DeviceRole role, MonitoringForegroundState state) =>
        PopulateNotification(new Notification.Builder(this, ChannelId), role, state);

    private Notification BuildNotificationLegacy(DeviceRole role, MonitoringForegroundState state)
    {
#pragma warning disable CA1422 // Legacy builder нужен для API 24–25 (minSdk 24).
        return PopulateNotification(new Notification.Builder(this), role, state);
#pragma warning restore CA1422
    }

    private Notification PopulateNotification(
        Notification.Builder builder,
        DeviceRole role,
        MonitoringForegroundState state)
    {
        var openAppIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        if (openAppIntent is not null)
        {
            builder.SetContentIntent(PendingIntent.GetActivity(
                this,
                OpenRequestCode,
                openAppIntent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        }

        var stopIntent = new Intent(this, typeof(MonitoringForegroundService)).SetAction(ActionStop);
        var stopPendingIntent = PendingIntent.GetService(
            this,
            StopRequestCode,
            stopIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        builder
            .SetContentTitle(role is DeviceRole.Child ? TitleChild : TitleParent)
            .SetContentText(state is MonitoringForegroundState.Running ? TextRunning : TextStopped)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(state is MonitoringForegroundState.Running)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false);

        builder.AddAction(new Notification.Action.Builder(
            Icon.CreateWithResource(this, global::Android.Resource.Drawable.IcDialogInfo),
            ActionStopLabel,
            stopPendingIntent).Build());

        return builder.Build();
    }

    /// <summary>
    /// Binder для host-адаптера: отдаёт снимок и событие изменения, не раскрывая Activity.
    /// </summary>
    public sealed class LocalBinder : Binder
    {
        internal LocalBinder(MonitoringForegroundService service)
        {
            Service = service;
        }

        /// <summary>Сервис, к которому привязан host.</summary>
        public MonitoringForegroundService Service { get; }
    }
}
