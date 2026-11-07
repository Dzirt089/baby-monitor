using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Platform.Android.ForegroundServices;

/// <summary>
/// Android-адаптер foreground-сессии: запускает/останавливает MonitoringForegroundService и
/// наблюдает его immutable снимки через binder. Требует видимую Activity (явное действие
/// пользователя) и разрешение на уведомления.
/// </summary>
public sealed class AndroidForegroundSessionHost : IForegroundSessionHost, IDisposable
{
    private const string LogTag = "BabyMonitor.Foreground";
    private const int StartTimeoutSeconds = 10;

    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly Context _context;
    private readonly INotificationPermission _notificationPermission;
    private readonly IMicrophonePermission _microphonePermission;
    private readonly IActivityVisibility _activityVisibility;
    private readonly object _sync = new();
    private readonly ServiceConnection _connection;

    private MonitoringForegroundService? _service;
    private TaskCompletionSource<MonitoringForegroundService>? _bindCompletion;
    private ForegroundSessionSnapshot? _current;
    private int _disposed;

    /// <summary>Создаёт host foreground-сессии.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="notificationPermission">Порт разрешения на уведомления.</param>
    /// <param name="microphonePermission">Порт разрешения на микрофон (используется только Child).</param>
    /// <param name="activityVisibility">Провайдер видимости Activity.</param>
    public AndroidForegroundSessionHost(
        Context context,
        INotificationPermission notificationPermission,
        IMicrophonePermission microphonePermission,
        IActivityVisibility activityVisibility)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(notificationPermission);
        ArgumentNullException.ThrowIfNull(microphonePermission);
        ArgumentNullException.ThrowIfNull(activityVisibility);
        _context = context.ApplicationContext ?? context;
        _notificationPermission = notificationPermission;
        _microphonePermission = microphonePermission;
        _activityVisibility = activityVisibility;
        _connection = new ServiceConnection(this);
    }

    /// <inheritdoc />
    public ForegroundSessionSnapshot? Current
    {
        get
        {
            lock (_sync)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

    /// <inheritdoc />
    public async Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            return Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.InvalidRole, "Роль не поддерживает foreground-мониторинг."));
        }

        if (!_activityVisibility.IsVisible)
        {
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.BackgroundStartDenied,
                "Запуск мониторинга возможен только при видимом экране приложения."));
        }

        var permission = await _notificationPermission.EnsureGrantedAsync(cancellationToken);
        if (!permission.IsSuccess)
        {
            return Result<ForegroundSessionSnapshot>.Failure(permission.Error!);
        }

        if (role is DeviceRole.Child)
        {
            // Android 14+ требует RECORD_AUDIO для FGS-типа microphone; Parent сюда не попадает.
            var microphone = await _microphonePermission.EnsureGrantedAsync(cancellationToken);
            if (!microphone.IsSuccess)
            {
                return Result<ForegroundSessionSnapshot>.Failure(microphone.Error!);
            }
        }

        var bound = await EnsureBoundAsync(cancellationToken);
        if (!bound.IsSuccess)
        {
            return Result<ForegroundSessionSnapshot>.Failure(bound.Error!);
        }

        var service = bound.Value;
        var existing = service.Snapshot;
        if (existing is not null && !existing.IsTerminal)
        {
            return existing.Role == role
                ? Result<ForegroundSessionSnapshot>.Success(existing)
                : Result<ForegroundSessionSnapshot>.Failure(new Error(
                    ForegroundSessionErrorCodes.AlreadyRunning,
                    "Уже запущена foreground-сессия с другой ролью."));
        }

        var completion = new TaskCompletionSource<Result<ForegroundSessionSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshot(object? sender, ForegroundSessionSnapshot snapshot)
        {
            if (snapshot.Role != role)
            {
                return;
            }

            if (snapshot.State is MonitoringForegroundState.Running)
            {
                completion.TrySetResult(Result<ForegroundSessionSnapshot>.Success(snapshot));
            }
            else if (snapshot.State is MonitoringForegroundState.Failed && snapshot.Error is not null)
            {
                completion.TrySetResult(Result<ForegroundSessionSnapshot>.Failure(snapshot.Error));
            }
        }

        service.SnapshotChanged += OnSnapshot;
        try
        {
            // Permission-диалог мог поставить Activity на паузу; повторно проверяем видимость.
            if (!_activityVisibility.IsVisible)
            {
                return Result<ForegroundSessionSnapshot>.Failure(new Error(
                    ForegroundSessionErrorCodes.BackgroundStartDenied,
                    "Запуск мониторинга возможен только при видимом экране приложения."));
            }

            var intent = new Intent(_context, typeof(MonitoringForegroundService))
                .SetAction(MonitoringForegroundService.ActionStart)
                .PutExtra(MonitoringForegroundService.ExtraRole, role.ToString());

            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                _context.StartForegroundService(intent);
            }
            else
            {
                _context.StartService(intent);
            }

            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(StartTimeoutSeconds), cancellationToken);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogTag, "Foreground service did not confirm start in time.");
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.ServiceUnavailable,
                "Foreground service не подтвердил запуск."));
        }
        catch (ForegroundServiceStartNotAllowedException exception)
        {
            Log.Warn(LogTag, "Foreground service start not allowed: " + exception.GetType().Name);
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.BackgroundStartDenied,
                "Android запретил запуск foreground service из фона."));
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            Log.Warn(LogTag, "Foreground service start failed: " + exception.GetType().Name);
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.ServiceUnavailable,
                "Не удалось запустить foreground service."));
        }
        finally
        {
            service.SnapshotChanged -= OnSnapshot;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bound = await EnsureBoundAsync(cancellationToken);
        if (!bound.IsSuccess)
        {
            return Result<ForegroundSessionSnapshot>.Failure(bound.Error!);
        }

        var service = bound.Value;
        var existing = service.Snapshot;
        if (existing is null || existing.IsTerminal)
        {
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.NotRunning,
                "Нет активной foreground-сессии."));
        }

        var completion = new TaskCompletionSource<Result<ForegroundSessionSnapshot>>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnSnapshot(object? sender, ForegroundSessionSnapshot snapshot)
        {
            if (snapshot.Role == existing.Role && snapshot.State is MonitoringForegroundState.Stopped)
            {
                completion.TrySetResult(Result<ForegroundSessionSnapshot>.Success(snapshot));
            }
        }

        service.SnapshotChanged += OnSnapshot;
        try
        {
            // Permission-диалог мог поставить Activity на паузу; повторно проверяем видимость.
            if (!_activityVisibility.IsVisible)
            {
                return Result<ForegroundSessionSnapshot>.Failure(new Error(
                    ForegroundSessionErrorCodes.BackgroundStartDenied,
                    "Запуск мониторинга возможен только при видимом экране приложения."));
            }

            var intent = new Intent(_context, typeof(MonitoringForegroundService))
                .SetAction(MonitoringForegroundService.ActionStop);
            _context.StartService(intent);
            return await completion.Task.WaitAsync(StopTimeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogTag, "Foreground service did not confirm stop in time.");
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.ServiceUnavailable,
                "Foreground service не подтвердил остановку."));
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            Log.Warn(LogTag, "Foreground service stop failed: " + exception.GetType().Name);
            return Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.ServiceUnavailable,
                "Не удалось остановить foreground service."));
        }
        finally
        {
            service.SnapshotChanged -= OnSnapshot;
        }
    }

    /// <inheritdoc />
    public async Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var bound = await EnsureBoundAsync(cancellationToken);
        if (!bound.IsSuccess)
        {
            return Result<ForegroundSessionSnapshot>.Failure(bound.Error!);
        }

        var snapshot = bound.Value.Snapshot;
        return snapshot is null
            ? Result<ForegroundSessionSnapshot>.Failure(new Error(
                ForegroundSessionErrorCodes.NotRunning,
                "Нет активной foreground-сессии."))
            : Result<ForegroundSessionSnapshot>.Success(snapshot);
    }

    /// <summary>Отвязывается от service; дальнейшие Start/Stop недоступны.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        MonitoringForegroundService? service;
        lock (_sync)
        {
            service = _service;
            _service = null;
            _bindCompletion = null;
        }

        if (service is not null)
        {
            service.SnapshotChanged -= OnServiceSnapshotChanged;
        }

        try
        {
            _context.UnbindService(_connection);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "UnbindService failed: " + exception.GetType().Name);
        }
    }

    private async Task<Result<MonitoringForegroundService>> EnsureBoundAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<MonitoringForegroundService> completion;
        lock (_sync)
        {
            if (_service is not null)
            {
                return Result<MonitoringForegroundService>.Success(_service);
            }

            if (_bindCompletion is not null)
            {
                completion = _bindCompletion;
            }
            else
            {
                completion = new TaskCompletionSource<MonitoringForegroundService>(TaskCreationOptions.RunContinuationsAsynchronously);
                _bindCompletion = completion;
                var intent = new Intent(_context, typeof(MonitoringForegroundService));
                bool bound;
                try
                {
                    bound = _context.BindService(intent, _connection, Bind.AutoCreate);
                }
                catch (Exception exception)
                {
                    Log.Warn(LogTag, "BindService failed: " + exception.GetType().Name);
                    _bindCompletion = null;
                    return Result<MonitoringForegroundService>.Failure(new Error(
                        ForegroundSessionErrorCodes.ServiceUnavailable,
                        "Не удалось подключиться к foreground service."));
                }

                if (!bound)
                {
                    _bindCompletion = null;
                    return Result<MonitoringForegroundService>.Failure(new Error(
                        ForegroundSessionErrorCodes.ServiceUnavailable,
                        "Android отклонил привязку к foreground service."));
                }

            }
        }

        try
        {
            var service = await completion.Task.WaitAsync(BindTimeout, cancellationToken);
            return Result<MonitoringForegroundService>.Success(service);
        }
        catch (TimeoutException)
        {
            Log.Warn(LogTag, "Foreground service bind timed out.");
            lock (_sync)
            {
                if (ReferenceEquals(_bindCompletion, completion))
                {
                    _bindCompletion = null;
                }
            }

            return Result<MonitoringForegroundService>.Failure(new Error(
                ForegroundSessionErrorCodes.ServiceUnavailable,
                "Таймаут подключения к foreground service."));
        }
    }

    private void OnServiceAttached(MonitoringForegroundService service)
    {
        TaskCompletionSource<MonitoringForegroundService>? completion;
        lock (_sync)
        {
            _service = service;
            completion = _bindCompletion;
            _bindCompletion = null;
        }

        service.SnapshotChanged += OnServiceSnapshotChanged;
        completion?.TrySetResult(service);
        UpdateCurrent(service.Snapshot);
    }

    private void OnServiceDetached()
    {
        MonitoringForegroundService? service;
        lock (_sync)
        {
            service = _service;
            _service = null;
        }

        if (service is not null)
        {
            service.SnapshotChanged -= OnServiceSnapshotChanged;
        }

        var current = Current;
        if (current is not null && !current.IsTerminal)
        {
            UpdateCurrent(new ForegroundSessionSnapshot(
                current.Role,
                MonitoringForegroundState.Failed,
                new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Foreground service был остановлен системой.")));
        }
    }

    private void OnServiceSnapshotChanged(object? sender, ForegroundSessionSnapshot snapshot) => UpdateCurrent(snapshot);

    private void UpdateCurrent(ForegroundSessionSnapshot? snapshot)
    {
        lock (_sync)
        {
            _current = snapshot;
        }

        if (snapshot is not null)
        {
            SnapshotChanged?.Invoke(this, snapshot);
        }
    }

    private sealed class ServiceConnection : Java.Lang.Object, IServiceConnection
    {
        private readonly AndroidForegroundSessionHost _owner;

        public ServiceConnection(AndroidForegroundSessionHost owner) => _owner = owner;

        public void OnServiceConnected(ComponentName? name, IBinder? service)
        {
            if (service is MonitoringForegroundService.LocalBinder binder)
            {
                _owner.OnServiceAttached(binder.Service);
            }
            else
            {
                Log.Warn(LogTag, "Foreground service returned unexpected binder.");
                _owner.OnServiceDetached();
            }
        }

        public void OnServiceDisconnected(ComponentName? name) => _owner.OnServiceDetached();

        public void OnBindingDied(ComponentName? name) => _owner.OnServiceDetached();

        public void OnNullBinding(ComponentName? name) => _owner.OnServiceDetached();
    }
}
