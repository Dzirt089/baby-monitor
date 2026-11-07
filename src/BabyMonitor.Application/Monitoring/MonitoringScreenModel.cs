using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Monitoring;

/// <summary>
/// Presentation-agnostic ViewModel экрана мониторинга: вызывает только IForegroundSessionHost и
/// наблюдает immutable snapshot. Строки/UI-ресурсы остаются в Presentation; здесь только состояние.
/// </summary>
public sealed class MonitoringScreenModel : IDisposable
{
    private readonly IForegroundSessionHost _host;
    private readonly DeviceRole _role;
    private readonly EventHandler<ForegroundSessionSnapshot> _snapshotHandler;
    private bool _disposed;
    private bool _busy;

    /// <summary>Создаёт модель экрана.</summary>
    /// <param name="host">Порт foreground-сессии.</param>
    /// <param name="role">Роль Child или Parent.</param>
    /// <exception cref="ArgumentException">Роль Unknown.</exception>
    public MonitoringScreenModel(IForegroundSessionHost host, DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль должна быть Child или Parent.", nameof(role));
        }

        _host = host;
        _role = role;
        _snapshotHandler = OnSnapshotChanged;
        _host.SnapshotChanged += _snapshotHandler;
    }

    /// <summary>Роль экрана.</summary>
    public DeviceRole Role => _role;

    /// <summary>Последний snapshot; null до первого запуска/после остановки.</summary>
    public ForegroundSessionSnapshot? Snapshot { get; private set; }

    /// <summary>Идёт start/stop.</summary>
    public bool IsBusy => _busy;

    /// <summary>Инициализация завершена (можно разрешать Start).</summary>
    public bool IsInitialized { get; private set; }

    /// <summary>Код безопасной ошибки (без секретов).</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>Безопасное сообщение ошибки.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>Запущенный сервис имеет другую роль (Start заблокирован).</summary>
    public bool IsRoleMismatch { get; private set; }

    /// <summary>Уведомление об изменении состояния.</summary>
    public event EventHandler? Changed;

    /// <summary>Читает текущий snapshot; NotRunning — нормальное состояние, не ошибка.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _host.GetSnapshotAsync(cancellationToken);
            if (result.IsSuccess)
            {
                Apply(result.Value);
            }
            else if (result.Error!.Code != ForegroundSessionErrorCodes.NotRunning)
            {
                SetError(result.Error);
            }
        }
        finally
        {
            IsInitialized = true;
            RaiseChanged();
        }
    }

    /// <summary>Запускает foreground-сессию.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_busy || IsRoleMismatch)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _host.StartAsync(_role, cancellationToken);
            if (result.IsSuccess)
            {
                ClearError();
                Apply(result.Value);
            }
            else
            {
                SetError(result.Error!);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <summary>Останавливает foreground-сессию.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_busy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _host.StopAsync(cancellationToken);
            if (result.IsSuccess)
            {
                ClearError();
                Apply(result.Value);
            }
            else if (result.Error!.Code != ForegroundSessionErrorCodes.NotRunning)
            {
                SetError(result.Error);
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.SnapshotChanged -= _snapshotHandler;
    }

    private void OnSnapshotChanged(object? sender, ForegroundSessionSnapshot snapshot) => Apply(snapshot);

    private void Apply(ForegroundSessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        IsRoleMismatch = snapshot.Role != _role && !snapshot.IsTerminal;
        ClearError();
        RaiseChanged();
    }

    private void SetError(Application.Results.Error error)
    {
        ErrorCode = error.Code;
        ErrorMessage = error.Message;
        RaiseChanged();
    }

    private void ClearError()
    {
        ErrorCode = null;
        ErrorMessage = null;
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
