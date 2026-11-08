using System.Runtime.Versioning;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics.Drawables;
using Android.Media;
using Android.OS;
using Android.Util;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Media;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.Platform.Android.ForegroundServices;

/// <summary>
/// Foreground service мониторинга: постоянное уведомление с ролью, состоянием и действием Stop.
/// Child объявляет тип microphone (AudioRecord), Parent — mediaPlayback (AudioTrack); с 005 phase 2
/// service владеет media-сеансом через Application-координатор и обновляет уведомление по его
/// состоянию (подключение/звук/переподключение/ошибка). Service не хранит ссылок на
/// Activity/View/ViewModel.
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
    private const string TextConnecting = "Подключение…";
    private const string TextMediaFlowing = "Звук передаётся";
    private const string TextDegraded = "Связь ухудшилась…";
    private const string TextReconnecting = "Переподключение…";
    private const string TextStopped = "Мониторинг остановлен";
    private const string ActionStopLabel = "Остановить";
    private const string MediaFailureMessage = "Media-сеанс завершился ошибкой.";
    private const string TextParentAlarm = "Связь потеряна!";
    private const string AlarmChannelId = "babymonitor_parent_alarm";
    private const string AlarmChannelName = "Тревога связи";
    private const string AlarmChannelDescription = "Локальная тревога при потере связи с детским устройством";
    private const string AlarmTitle = "Радионяня: связь потеряна";
    private const string AlarmText = "Нет связи с детским устройством. Проверьте соединение.";
    private const int AlarmNotificationId = 1004;
    private const int AlarmOpenRequestCode = 1005;
    private const string NoMediaState = "none";

    private readonly object _sync = new();
    private readonly LocalBinder _binder;
    private ForegroundSessionSnapshot? _snapshot;
    private MonitoringMediaSession? _mediaSession;
    private CancellationTokenSource? _mediaCts;
    private Task? _mediaLifetime;
    private int _mediaGeneration;
    private bool _parentAlarmActive;
    private bool _keepParentAlarmOnStop;
    private string? _lastNotificationFingerprint;

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
        DismissParentAlarmOnStop();
        ResetNotificationFingerprint();
        StopMediaSession();
        base.OnDestroy();
    }

    private void HandleStart(Intent intent)
    {
        var roleText = intent.GetStringExtra(ExtraRole);
        var role = roleText switch
        {
            nameof(DeviceRole.Child) => DeviceRole.Child,
            nameof(DeviceRole.Parent) => DeviceRole.Parent,
            _ => DeviceRole.Unknown
        };
        if (role is DeviceRole.Unknown)
        {
            Log.Warn(LogTag, "Foreground service start rejected: invalid role.");
            PublishTerminalIfNeeded();
            StopSelf();
            return;
        }

        // Новый запуск не должен сохранять stale standalone alarm предыдущей упавшей сессии.
        DismissParentAlarmOnRestart();
        ResetNotificationFingerprint();
        if (role is DeviceRole.Parent)
        {
            // Канал тревоги нужен только Parent (L5); для API 24–25 каналов нет — legacy builder.
            CreateAlarmNotificationChannel();
        }

        try
        {
            var notification = BuildNotification(role, MonitoringForegroundState.Running, media: null);
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
            StartMediaSession(role);
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

    private void StartMediaSession(DeviceRole role)
    {
        lock (_sync)
        {
            if (_mediaSession is not null)
            {
                // Защита от гонки повторного ActionStart: активный сеанс не осиротеваем.
                return;
            }
        }

        if (Application is not IMonitoringMediaSessionFactoryProvider provider)
        {
            PublishMediaFailure(role, new Error(
                MediaSessionErrorCodes.AdapterFailure,
                "Media-сеанс недоступен: composition root не инициализирован."));
            return;
        }

        var generation = Interlocked.Increment(ref _mediaGeneration);
        var cts = new CancellationTokenSource();
        lock (_sync)
        {
            _mediaCts = cts;
        }

        _mediaLifetime = RunMediaSessionAsync(provider, role, generation, cts.Token);
    }

    private async Task RunMediaSessionAsync(
        IMonitoringMediaSessionFactoryProvider provider,
        DeviceRole role,
        int generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var created = await provider.MediaSessions.CreateAsync(role, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                PublishMediaFailure(role, created.Error!);
                return;
            }

            var session = created.Value;
            bool stale;
            lock (_sync)
            {
                stale = generation != _mediaGeneration;
                if (!stale)
                {
                    _mediaSession = session;
                }
            }

            if (stale)
            {
                // Сеанс остановлен/перезапущен, пока шло создание: освобождаем результат.
                await DisposeMediaSessionAsync(session).ConfigureAwait(false);
                return;
            }

            session.Coordinator.SnapshotChanged += OnMediaSnapshotChanged;
            var started = await session.Coordinator.StartAsync(cancellationToken).ConfigureAwait(false);
            if (!started.IsSuccess)
            {
                PublishMediaFailure(role, started.Error!);
                return;
            }

            PublishComposed(role, MonitoringForegroundState.Running, error: null, started.Value);
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка сервиса/сеанса.
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Media session start failed: " + exception.GetType().Name);
            PublishMediaFailure(role, new Error(MediaSessionErrorCodes.AdapterFailure, MediaFailureMessage));
        }
    }

    private void OnMediaSnapshotChanged(object? sender, MediaSessionSnapshot media)
    {
        try
        {
            if (media.State is MediaSessionState.Failed)
            {
                PublishFailedMedia(media);
                return;
            }

            PublishComposed(media.Role, MonitoringForegroundState.Running, error: null, media);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Media snapshot handling failed: " + exception.GetType().Name);
        }
    }

    private void PublishComposed(
        DeviceRole role,
        MonitoringForegroundState state,
        Error? error,
        MediaSessionSnapshot? media)
    {
        var snapshot = new ForegroundSessionSnapshot(role, state, error, media);
        Publish(snapshot);
    }

    private void PublishMediaFailure(DeviceRole role, Error error)
    {
        Publish(new ForegroundSessionSnapshot(role, MonitoringForegroundState.Failed, error));
        StopSession();
    }

    /// <summary>
    /// Публикует терминальный Failed media-снимок ВМЕСТЕ с media: UI видит ParentAlarmActive и
    /// показывает «Связь потеряна!», а high-importance alarm Parent переживает остановку сервиса.
    /// </summary>
    private void PublishFailedMedia(MediaSessionSnapshot media)
    {
        var error = media.Error ?? new Error(MediaSessionErrorCodes.AdapterFailure, MediaFailureMessage);
        Publish(new ForegroundSessionSnapshot(media.Role, MonitoringForegroundState.Failed, error, media));

        if (media.Role is DeviceRole.Parent && media.ParentAlarmActive)
        {
            lock (_sync)
            {
                // Alarm уже поднят внутри Publish → UpdateParentLossAlarm; StopSession его не снимает.
                _keepParentAlarmOnStop = true;
            }
        }

        StopSession();
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
        DismissParentAlarmOnStop();
        ResetNotificationFingerprint();
        StopMediaSession();
        StopSelf();
    }

    private void StopMediaSession()
    {
        MonitoringMediaSession? session;
        CancellationTokenSource? cts;
        lock (_sync)
        {
            Interlocked.Increment(ref _mediaGeneration);
            session = _mediaSession;
            _mediaSession = null;
            cts = _mediaCts;
            _mediaCts = null;
        }

        if (session is not null)
        {
            session.Coordinator.SnapshotChanged -= OnMediaSnapshotChanged;
            _mediaLifetime = DisposeMediaSessionAsync(session);
        }

        try
        {
            cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Уже отменён/освобождён — штатно.
        }
        finally
        {
            cts?.Dispose();
        }
    }

    /// <summary>
    /// Освобождает media-сеанс best-effort: сбой Dispose не должен подменять исход сеанса и
    /// блокировать освобождение остальных ресурсов (Application без logging).
    /// </summary>
    private static async Task DisposeMediaSessionAsync(MonitoringMediaSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: см. XML-doc.
        }
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

        if (snapshot.State is MonitoringForegroundState.Running)
        {
            UpdateNotification(snapshot);
        }

        UpdateParentLossAlarm(snapshot);
        SnapshotChanged?.Invoke(this, snapshot);
    }

    private void UpdateNotification(ForegroundSessionSnapshot snapshot)
    {
        var fingerprint = BuildNotificationFingerprint(snapshot);
        lock (_sync)
        {
            if (string.Equals(_lastNotificationFingerprint, fingerprint, StringComparison.Ordinal))
            {
                // Live-refresh snapshot (~1 раз/с): визуально ничего не изменилось — не дёргаем Notify.
                return;
            }
        }

        try
        {
            var notification = BuildNotification(snapshot.Role, snapshot.State, snapshot.Media);
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            if (manager is null)
            {
                return;
            }

            manager.Notify(NotificationId, notification);
            lock (_sync)
            {
                _lastNotificationFingerprint = fingerprint;
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Notification update failed: " + exception.GetType().Name);
        }
    }

    /// <summary>
    /// Безопасный fingerprint постоянного уведомления: роль, foreground-состояние, media-состояние,
    /// флаг alarm Parent и код ошибки media. Не содержит секретов, аудио, сообщений ошибок и PII.
    /// </summary>
    private static string BuildNotificationFingerprint(ForegroundSessionSnapshot snapshot)
    {
        var mediaState = snapshot.Media?.State.ToString() ?? NoMediaState;
        var parentAlarm = snapshot.Media?.ParentAlarmActive == true ? "alarm" : "no-alarm";
        var errorCode = snapshot.Media?.Error?.Code ?? string.Empty;
        return string.Concat(
            snapshot.Role.ToString(),
            "|",
            snapshot.State.ToString(),
            "|",
            mediaState,
            "|",
            parentAlarm,
            "|",
            errorCode);
    }

    /// <summary>Сбрасывает fingerprint, чтобы следующий Running snapshot гарантированно обновил уведомление.</summary>
    private void ResetNotificationFingerprint()
    {
        lock (_sync)
        {
            _lastNotificationFingerprint = null;
        }
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

    /// <summary>
    /// Создаёт отдельный high-importance канал тревоги (звук/вибрация по умолчанию) только для
    /// Parent. Best-effort: отсутствие права на звук/вибрацию не должно ломать foreground service.
    /// </summary>
    /// <remarks>
    /// Device-only: звук/вибрация/DND канала не покрыты unit-тестами Platform.Android;
    /// manifest/VIBRATE не меняются. Для API 24–25 каналов нет — используется legacy builder.
    /// </remarks>
    private void CreateAlarmNotificationChannel()
    {
        if (!OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return;
        }

        try
        {
            CreateAlarmNotificationChannelApi26();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Alarm channel creation failed: " + exception.GetType().Name);
        }
    }

    [SupportedOSPlatform("android26.0")]
    private void CreateAlarmNotificationChannelApi26()
    {
        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager is null)
        {
            return;
        }

        var channel = new NotificationChannel(
            AlarmChannelId,
            AlarmChannelName,
            NotificationImportance.High)
        {
            Description = AlarmChannelDescription
        };
        channel.EnableVibration(true);
        channel.SetShowBadge(true);

        var sound = RingtoneManager.GetDefaultUri(RingtoneType.Notification);
        if (sound is not null)
        {
            var attributesBuilder = new AudioAttributes.Builder();
            attributesBuilder.SetUsage(AudioUsageKind.Notification);
            attributesBuilder.SetContentType(AudioContentType.Sonification);
            var attributes = attributesBuilder.Build();
            if (attributes is not null)
            {
                channel.SetSound(sound, attributes);
                attributes.Dispose();
            }

            attributesBuilder.Dispose();
        }

        manager.CreateNotificationChannel(channel);
    }

    /// <summary>
    /// Transition-based тревога Parent: high-importance уведомление поднимается только на
    /// false→true и снимается на true→false/остановке; каждый SnapshotChanged не спамит.
    /// ParentAlarmActive учитывается независимо от состояния: терминальный Failed Parent тоже
    /// показывает alarm (уведомление остаётся standalone после остановки сервиса).
    /// </summary>
    /// <remarks>
    /// Device-only: путь уведомлений Platform.Android не покрыт unit-тестами (нет test infra);
    /// звук, вибрация, heads-up и Do Not Disturb проверяются на устройстве. Manifest/VIBRATE не
    /// меняются; без права вибрации уведомление деградирует best-effort.
    /// </remarks>
    private void UpdateParentLossAlarm(ForegroundSessionSnapshot snapshot)
    {
        var isActive = snapshot.Role is DeviceRole.Parent && snapshot.Media?.ParentAlarmActive is true;

        bool raise;
        bool cancel;
        lock (_sync)
        {
            if (isActive == _parentAlarmActive)
            {
                raise = false;
                cancel = false;
            }
            else
            {
                _parentAlarmActive = isActive;
                raise = isActive;
                cancel = !isActive;
                if (!isActive && snapshot.State is MonitoringForegroundState.Running)
                {
                    // Успешное восстановление: отложенное сохранение alarm больше не нужно.
                    _keepParentAlarmOnStop = false;
                }
            }
        }

        if (raise)
        {
            ShowParentAlarmNotification();
        }
        else if (cancel)
        {
            bool keep;
            lock (_sync)
            {
                keep = _keepParentAlarmOnStop;
            }

            if (!keep)
            {
                CancelParentAlarmNotification();
            }
        }
    }

    /// <summary>Показывает alarm-уведомление best-effort: отказ в notifications/звуке не роняет сервис.</summary>
    private void ShowParentAlarmNotification()
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Notify(AlarmNotificationId, BuildParentAlarmNotification());
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Parent alarm notification failed: " + exception.GetType().Name);
        }
    }

    /// <summary>Снимает alarm-уведомление и сбрасывает флаг; идемпотентно.</summary>
    private void CancelParentAlarmNotification()
    {
        lock (_sync)
        {
            _parentAlarmActive = false;
        }

        CancelAlarmNotificationOnly();
    }

    /// <summary>
    /// Остановка сервиса: standalone alarm терминального Failed Parent сохраняется (AutoCancel
    /// снимет его по тапу пользователя); в остальных случаях уведомление снимается.
    /// </summary>
    private void DismissParentAlarmOnStop()
    {
        bool keep;
        lock (_sync)
        {
            keep = _keepParentAlarmOnStop;
            _parentAlarmActive = false;
        }

        if (keep)
        {
            return;
        }

        CancelAlarmNotificationOnly();
    }

    /// <summary>Новый запуск сервиса: stale standalone alarm предыдущей сессии снимается.</summary>
    private void DismissParentAlarmOnRestart()
    {
        lock (_sync)
        {
            _parentAlarmActive = false;
            _keepParentAlarmOnStop = false;
        }

        CancelAlarmNotificationOnly();
    }

    private void CancelAlarmNotificationOnly()
    {
        try
        {
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.Cancel(AlarmNotificationId);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Parent alarm cancel failed: " + exception.GetType().Name);
        }
    }

    private Notification BuildParentAlarmNotification()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return BuildParentAlarmNotificationApi26();
        }

        return BuildParentAlarmNotificationLegacy();
    }

    [SupportedOSPlatform("android26.0")]
    private Notification BuildParentAlarmNotificationApi26() =>
        PopulateParentAlarmNotification(new Notification.Builder(this, AlarmChannelId));

    private Notification BuildParentAlarmNotificationLegacy()
    {
#pragma warning disable CA1422 // Legacy builder нужен для API 24–25 (minSdk 24).
        var builder = new Notification.Builder(this);
        builder.SetDefaults(NotificationDefaults.Sound | NotificationDefaults.Vibrate);
        builder.SetPriority((int)NotificationPriority.High);
#pragma warning restore CA1422
        return PopulateParentAlarmNotification(builder);
    }

    private Notification PopulateParentAlarmNotification(Notification.Builder builder)
    {
        var openAppIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        if (openAppIntent is not null)
        {
            builder.SetContentIntent(PendingIntent.GetActivity(
                this,
                AlarmOpenRequestCode,
                openAppIntent,
                PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent));
        }

        builder
            .SetContentTitle(AlarmTitle)
            .SetContentText(AlarmText)
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogAlert)
            .SetAutoCancel(true)
            .SetOngoing(false)
            .SetCategory(Notification.CategoryError);

        return builder.Build();
    }

    private Notification BuildNotification(
        DeviceRole role,
        MonitoringForegroundState state,
        MediaSessionSnapshot? media)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return BuildNotificationApi26(role, state, media);
        }

        return BuildNotificationLegacy(role, state, media);
    }

    [SupportedOSPlatform("android26.0")]
    private Notification BuildNotificationApi26(
        DeviceRole role,
        MonitoringForegroundState state,
        MediaSessionSnapshot? media) =>
        PopulateNotification(new Notification.Builder(this, ChannelId), role, state, media);

    private Notification BuildNotificationLegacy(
        DeviceRole role,
        MonitoringForegroundState state,
        MediaSessionSnapshot? media)
    {
#pragma warning disable CA1422 // Legacy builder нужен для API 24–25 (minSdk 24).
        return PopulateNotification(new Notification.Builder(this), role, state, media);
#pragma warning restore CA1422
    }

    private static string GetNotificationText(MonitoringForegroundState state, MediaSessionSnapshot? media)
    {
        if (media is not null)
        {
            if (media.Role is DeviceRole.Parent && media.ParentAlarmActive)
            {
                return TextParentAlarm;
            }

            return media.State switch
            {
                MediaSessionState.MediaFlowing => TextMediaFlowing,
                MediaSessionState.Degraded => TextDegraded,
                MediaSessionState.Reconnecting => TextReconnecting,
                MediaSessionState.Failed => TextStopped,
                MediaSessionState.Stopped => TextStopped,
                MediaSessionState.Idle => state is MonitoringForegroundState.Running ? TextRunning : TextStopped,
                _ => TextConnecting
            };
        }

        return state is MonitoringForegroundState.Running ? TextRunning : TextStopped;
    }

    private Notification PopulateNotification(
        Notification.Builder builder,
        DeviceRole role,
        MonitoringForegroundState state,
        MediaSessionSnapshot? media)
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
            .SetContentText(GetNotificationText(state, media))
            .SetSmallIcon(global::Android.Resource.Drawable.IcDialogInfo)
            .SetOngoing(state is MonitoringForegroundState.Running)
            .SetOnlyAlertOnce(true)
            .SetShowWhen(false);

        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // Android 12+ иначе может отложить foreground-уведомление (и Stop) до ~10 секунд.
            builder.SetForegroundServiceBehavior((int)NotificationForegroundService.Immediate);
        }

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
