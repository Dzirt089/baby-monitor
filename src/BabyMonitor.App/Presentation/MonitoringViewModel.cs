using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.App.Presentation;

/// <summary>
/// ViewModel экрана мониторинга: вызывает только IForegroundSessionHost и наблюдает immutable
/// snapshot; не содержит Android-типов и бизнес-логики вне представления состояния.
/// </summary>
public sealed class MonitoringViewModel
{
    private readonly IForegroundSessionHost _host;
    private readonly DeviceRole _role;

    /// <summary>Создаёт ViewModel для роли.</summary>
    /// <param name="host">Порт foreground-сессии.</param>
    /// <param name="role">Child или Parent.</param>
    public MonitoringViewModel(IForegroundSessionHost host, DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль должна быть Child или Parent.", nameof(role));
        }

        _host = host;
        _role = role;
        _host.SnapshotChanged += OnHostSnapshotChanged;
    }

    /// <summary>Роль экрана.</summary>
    public DeviceRole Role => _role;

    /// <summary>Последний snapshot; null до первого запуска.</summary>
    public ForegroundSessionSnapshot? Snapshot { get; private set; }

    /// <summary>Текст состояния для UI.</summary>
    public string StatusText { get; private set; } = "Ожидание";

    /// <summary>Диагностика без секретов.</summary>
    public string DiagnosticsText { get; private set; } = "Media недоступна (005 phase 2 BLOCKED).";

    /// <summary>Безопасный текст ошибки (code + message).</summary>
    public string? ErrorText { get; private set; }

    /// <summary>Идёт операция start/stop.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Изменение состояния ViewModel.</summary>
    public event EventHandler? Changed;

    /// <summary>Читает текущий snapshot (например, после recreate).</summary>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var result = await _host.GetSnapshotAsync(cancellationToken);
        if (result.IsSuccess)
        {
            Apply(result.Value);
        }
        else
        {
            SetIdle(result.Error!);
        }
    }

    /// <summary>Запускает foreground-сессию.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorText = null;
        try
        {
            var result = await _host.StartAsync(_role, cancellationToken);
            if (result.IsSuccess)
            {
                Apply(result.Value);
            }
            else
            {
                ErrorText = result.Error!.Code + ": " + result.Error.Message;
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
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        ErrorText = null;
        try
        {
            var result = await _host.StopAsync(cancellationToken);
            if (result.IsSuccess)
            {
                Apply(result.Value);
            }
            else
            {
                ErrorText = result.Error!.Code + ": " + result.Error.Message;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnHostSnapshotChanged(object? sender, ForegroundSessionSnapshot snapshot) => Apply(snapshot);

    private void Apply(ForegroundSessionSnapshot snapshot)
    {
        Snapshot = snapshot;
        StatusText = snapshot.State switch
        {
            MonitoringForegroundState.Starting => "Запуск…",
            MonitoringForegroundState.Running => "Мониторинг активен",
            MonitoringForegroundState.Stopping => "Остановка…",
            MonitoringForegroundState.Stopped => "Остановлен",
            MonitoringForegroundState.Failed => "Ошибка",
            _ => "Неизвестно"
        };
        DiagnosticsText = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Роль: {RoleDisplay(snapshot.Role)} · Состояние: {snapshot.State} · Media: недоступна (005 BLOCKED)");
        ErrorText = snapshot.Error is null ? ErrorText : snapshot.Error.Code + ": " + snapshot.Error.Message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetIdle(Application.Results.Error error)
    {
        Snapshot = null;
        StatusText = "Не запущено";
        DiagnosticsText = "Роль: " + RoleDisplay(_role) + " · Media: недоступна (005 BLOCKED)";
        ErrorText = error.Code + ": " + error.Message;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SetBusy(bool busy)
    {
        IsBusy = busy;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static string RoleDisplay(DeviceRole role) => role is DeviceRole.Child ? "Ребёнок" : "Родитель";
}
