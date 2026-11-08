using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;

namespace BabyMonitor.App.Presentation;

/// <summary>
/// Статус экрана «Обновления» для маппинга в строки ресурсов (Activity не содержит бизнес-логики).
/// </summary>
public enum UpdateScreenStatus
{
    /// <summary>Проверка ещё не выполнялась.</summary>
    Idle = 0,

    /// <summary>Идёт проверка манифеста.</summary>
    Checking = 1,

    /// <summary>Установлена последняя доступная версия.</summary>
    UpToDate = 2,

    /// <summary>Найдено более новое обновление; можно скачать и установить.</summary>
    UpdateAvailable = 3,

    /// <summary>Идёт загрузка APK с проверкой размера/SHA-256.</summary>
    Downloading = 4,

    /// <summary>PackageInstaller-сессия создана: пользователь подтверждает установку в системе.</summary>
    InstallRequested = 5,

    /// <summary>Последняя операция завершилась типизированной ошибкой.</summary>
    Failed = 6,

    /// <summary>Установка APK подтверждена системой.</summary>
    InstallSucceeded = 7,

    /// <summary>Пользователь отменил установку APK.</summary>
    InstallCancelled = 8
}

/// <summary>
/// ViewModel экрана «Обновления» (009, ADR-0012): вызывает Application use case
/// <see cref="AppUpdateCoordinator"/>, хранит состояние для Activity и не содержит текстов/Android View.
/// <see cref="Changed"/> может подниматься с фонового потока — Activity переключается в UI-поток.
/// Секреты/PII не хранит и не логирует.
/// </summary>
public sealed class UpdateViewModel : IDisposable
{
    private readonly AppUpdateCoordinator _coordinator;
    private readonly IUpdateSettingsStore _settings;
    private readonly IAppVersionProvider _versionProvider;
    private readonly IUpdateInstallStatusSource _installStatusSource;
    private readonly EventHandler<UpdateInstallOutcomeEventArgs> _installOutcomeHandler;
    private readonly object _sync = new();
    private readonly string _currentVersionName;
    private int _busy;
    private bool _disposed;
    private UpdateScreenStatus _status;
    private AppUpdateInfo? _availableUpdate;
    private string? _errorCode;
    private string? _errorMessage;
    private double _downloadProgress;
    private bool _autoCheckEnabled;
    private bool _installOutcomeApplied;

    /// <summary>Создаёт ViewModel экрана обновлений.</summary>
    /// <param name="coordinator">Application use case проверки/скачивания/установки.</param>
    /// <param name="settings">Сохранённые настройки автообновления.</param>
    /// <param name="versionProvider">Провайдер установленной версии.</param>
    /// <param name="installStatusSource">Источник итога установки APK (live + persisted).</param>
    /// <exception cref="ArgumentNullException">Любой аргумент равен <see langword="null"/>.</exception>
    public UpdateViewModel(
        AppUpdateCoordinator coordinator,
        IUpdateSettingsStore settings,
        IAppVersionProvider versionProvider,
        IUpdateInstallStatusSource installStatusSource)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(installStatusSource);

        _coordinator = coordinator;
        _settings = settings;
        _versionProvider = versionProvider;
        _installStatusSource = installStatusSource;
        _installOutcomeHandler = OnInstallOutcomeChanged;
        _installStatusSource.OutcomeChanged += _installOutcomeHandler;
        _currentVersionName = versionProvider.GetCurrentVersion().VersionName;
        _autoCheckEnabled = settings.AutoCheckEnabled;
        _status = UpdateScreenStatus.Idle;
    }

    /// <summary>Состояние изменилось (может подниматься с фонового потока).</summary>
    public event EventHandler? Changed;

    /// <summary>Установленная версия приложения (versionName).</summary>
    public string CurrentVersionName => _currentVersionName;

    /// <summary>Текущий статус экрана.</summary>
    public UpdateScreenStatus Status
    {
        get
        {
            lock (_sync)
            {
                return _status;
            }
        }
    }

    /// <summary>Найденное обновление; null, если обновление не требуется/ещё не найдено.</summary>
    public AppUpdateInfo? AvailableUpdate
    {
        get
        {
            lock (_sync)
            {
                return _availableUpdate;
            }
        }
    }

    /// <summary>Идёт проверка/загрузка/установка (кнопки блокируются).</summary>
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    /// <summary>Типизированный код последней ошибки; null при успехе.</summary>
    public string? ErrorCode
    {
        get
        {
            lock (_sync)
            {
                return _errorCode;
            }
        }
    }

    /// <summary>Безопасное сообщение последней ошибки; null при успехе.</summary>
    public string? ErrorMessage
    {
        get
        {
            lock (_sync)
            {
                return _errorMessage;
            }
        }
    }

    /// <summary>Прогресс загрузки 0..1.</summary>
    public double DownloadProgress
    {
        get
        {
            lock (_sync)
            {
                return _downloadProgress;
            }
        }
    }

    /// <summary>Включена ли автоматическая проверка обновлений (persisted в настройках).</summary>
    public bool AutoCheckEnabled
    {
        get
        {
            lock (_sync)
            {
                return _autoCheckEnabled;
            }
        }
    }

    /// <summary>Читает сохранённые настройки; вызывать при создании экрана.</summary>
    public void Initialize()
    {
        var lastOutcome = _installStatusSource.TakeLastOutcome();
        if (lastOutcome is not null)
        {
            ApplyInstallOutcome(lastOutcome.Value);
        }

        lock (_sync)
        {
            _autoCheckEnabled = _settings.AutoCheckEnabled;
        }

        RaiseChanged();
    }

    /// <summary>Сохраняет флаг автопроверки (чекбокс на экране).</summary>
    /// <param name="enabled">Новое значение флага.</param>
    public void SetAutoCheckEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        lock (_sync)
        {
            _autoCheckEnabled = enabled;
        }

        _settings.AutoCheckEnabled = enabled;
        RaiseChanged();
    }

    /// <summary>Проверяет обновления вручную (кнопка «Проверить обновления»).</summary>
    /// <param name="cancellationToken">Отмена при уходе с экрана.</param>
    public async Task CheckAsync(CancellationToken cancellationToken)
    {
        if (!TryBeginOperation())
        {
            return;
        }

        SetChecking();
        try
        {
            var result = await _coordinator
                .CheckAsync(AppUpdateCheckTrigger.Manual, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                SetFailed(result.Error!);
            }
            else if (result.Value is null)
            {
                SetUpToDate();
            }
            else
            {
                SetUpdateAvailable(result.Value);
            }
        }
        catch (OperationCanceledException)
        {
            SetIdle();
        }
        finally
        {
            EndOperation();
        }
    }

    /// <summary>Скачивает и запускает установку найденного обновления (подтверждение — пользователь).</summary>
    /// <param name="cancellationToken">Отмена при уходе с экрана.</param>
    public async Task DownloadAndInstallAsync(CancellationToken cancellationToken)
    {
        AppUpdateInfo? update;
        lock (_sync)
        {
            update = _availableUpdate;
        }

        if (update is null || !TryBeginOperation())
        {
            return;
        }

        lock (_sync)
        {
            // Новый цикл установки: прежний итог установки больше не актуален для этого запуска.
            _installOutcomeApplied = false;
        }

        SetDownloading();
        try
        {
            var progress = new DownloadProgressReporter(this);
            var result = await _coordinator
                .DownloadAndInstallAsync(update.Manifest, progress, cancellationToken)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                SetInstallRequested();
            }
            else
            {
                SetFailed(result.Error!);
            }
        }
        catch (OperationCanceledException)
        {
            SetIdle();
        }
        finally
        {
            EndOperation();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _installStatusSource.OutcomeChanged -= _installOutcomeHandler;
    }

    private bool TryBeginOperation()
    {
        if (_disposed)
        {
            return false;
        }

        return Interlocked.CompareExchange(ref _busy, 1, 0) == 0;
    }

    private void EndOperation()
    {
        Volatile.Write(ref _busy, 0);
        RaiseChanged();
    }

    private void SetChecking()
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.Checking;
            _errorCode = null;
            _errorMessage = null;
        }

        RaiseChanged();
    }

    private void SetIdle()
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.Idle;
        }

        RaiseChanged();
    }

    private void SetUpToDate()
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.UpToDate;
            _availableUpdate = null;
            _errorCode = null;
            _errorMessage = null;
        }

        RaiseChanged();
    }

    private void SetUpdateAvailable(AppUpdateInfo update)
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.UpdateAvailable;
            _availableUpdate = update;
            _errorCode = null;
            _errorMessage = null;
        }

        RaiseChanged();
    }

    private void SetDownloading()
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.Downloading;
            _downloadProgress = 0;
            _errorCode = null;
            _errorMessage = null;
        }

        RaiseChanged();
    }

    private void SetInstallRequested()
    {
        lock (_sync)
        {
            if (_installOutcomeApplied)
            {
                // Итог установки (например, Failed из-за не запустившегося подтверждения) уже пришёл.
                return;
            }

            _status = UpdateScreenStatus.InstallRequested;
            _downloadProgress = 1;
        }

        RaiseChanged();
    }

    private void SetFailed(Error error)
    {
        lock (_sync)
        {
            _status = UpdateScreenStatus.Failed;
            _errorCode = error.Code;
            _errorMessage = error.Message;
        }

        RaiseChanged();
    }

    private void OnInstallOutcomeChanged(object? sender, UpdateInstallOutcomeEventArgs args)
    {
        if (_disposed)
        {
            return;
        }

        ApplyInstallOutcome(args.Outcome);
    }

    private void ApplyInstallOutcome(UpdateInstallOutcome outcome)
    {
        lock (_sync)
        {
            _installOutcomeApplied = true;
            _errorCode = null;
            _errorMessage = null;
            switch (outcome)
            {
                case UpdateInstallOutcome.Succeeded:
                    _status = UpdateScreenStatus.InstallSucceeded;
                    _downloadProgress = 1;
                    break;
                case UpdateInstallOutcome.Cancelled:
                    _status = UpdateScreenStatus.InstallCancelled;
                    break;
                default:
                    _status = UpdateScreenStatus.Failed;
                    _errorCode = AppUpdateErrorCodes.InstallFailed;
                    _errorMessage = "Установка обновления не завершена.";
                    break;
            }
        }

        RaiseChanged();
    }

    private void ReportDownloadProgress(double value)
    {
        var normalized = double.IsFinite(value) ? Math.Clamp(value, 0d, 1d) : 0d;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _downloadProgress = normalized;
        }

        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private sealed class DownloadProgressReporter : IProgress<double>
    {
        private readonly UpdateViewModel _owner;

        public DownloadProgressReporter(UpdateViewModel owner) => _owner = owner;

        public void Report(double value) => _owner.ReportDownloadProgress(value);
    }
}
