using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Diagnostics;

/// <summary>
/// Presentation-agnostic модель экрана диагностики: читает snapshot foreground-сессии и список
/// доверенных устройств, не раскрывая секретов и аудиоданных. Подписывается на изменения
/// foreground-сессии и обновляет уже построенный snapshot без повторного запроса к хранилищу.
/// </summary>
public sealed class DiagnosticsScreenModel : IDisposable
{
    private readonly IForegroundSessionHost _host;
    private readonly ITrustedDeviceStore _store;
    private readonly DeviceRole _role;
    private readonly EventHandler<ForegroundSessionSnapshot> _snapshotHandler;
    private bool _isBusy;
    private bool _disposed;
    private string? _errorCode;
    private string? _errorMessage;

    /// <summary>Создаёт модель экрана диагностики.</summary>
    /// <param name="host">Порт foreground-сессии.</param>
    /// <param name="store">Хранилище доверенных устройств.</param>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <exception cref="ArgumentNullException">Любой аргумент равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Роль не Child и не Parent.</exception>
    public DiagnosticsScreenModel(IForegroundSessionHost host, ITrustedDeviceStore store, DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(store);
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль должна быть Child или Parent.", nameof(role));
        }

        _host = host;
        _store = store;
        _role = role;
        _snapshotHandler = OnSnapshotChanged;
        _host.SnapshotChanged += _snapshotHandler;
    }

    /// <summary>Последний построенный снимок диагностики; <see langword="null"/> до первого Refresh.</summary>
    public DiagnosticsSnapshot? Snapshot { get; private set; }

    /// <summary>Идёт асинхронная операция.</summary>
    public bool IsBusy => _isBusy;

    /// <summary>Стабильный код последней ошибки (без секретов); <see langword="null"/> при успехе.</summary>
    public string? ErrorCode => _errorCode;

    /// <summary>Безопасное для показа сообщение последней ошибки; <see langword="null"/> при успехе.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>Уведомление об изменении наблюдаемого состояния.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Перечитывает foreground-сессию и доверенные устройства. Отсутствие запущенного сервиса
    /// (NotRunning) не является ошибкой: <see cref="DiagnosticsSnapshot.Foreground"/> будет null.
    /// </summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <remarks>
    /// При ошибке host или store снимок не обновляется, чтобы UI не показывал частичные данные.
    /// </remarks>
    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (_isBusy || _disposed)
        {
            return;
        }

        _isBusy = true;
        RaiseChanged();
        try
        {
            var foregroundResult = await _host.GetSnapshotAsync(cancellationToken);
            ForegroundSessionSnapshot? foreground;
            if (foregroundResult.IsSuccess)
            {
                foreground = foregroundResult.Value;
            }
            else
            {
                var error = foregroundResult.Error!;
                if (error.Code == ForegroundSessionErrorCodes.NotRunning)
                {
                    foreground = null;
                }
                else
                {
                    SetError(error);
                    return;
                }
            }

            var devicesResult = await _store.GetAllAsync(cancellationToken);
            if (!devicesResult.IsSuccess)
            {
                SetError(devicesResult.Error!);
                return;
            }

            var trustedDevices = devicesResult.Value
                .Where(device => device.LocalRole == _role)
                .OrderByDescending(device => device.PairedAt)
                .Select(PairedDeviceSummary.From)
                .ToArray();
            Snapshot = new DiagnosticsSnapshot(_role, foreground, trustedDevices);
            ClearError();
        }
        finally
        {
            _isBusy = false;
            RaiseChanged();
        }
    }

    /// <summary>
    /// Освобождает подписку на изменения foreground-сессии. Идемпотентно; модель не владеет
    /// host/store и не освобождает их.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.SnapshotChanged -= _snapshotHandler;
    }

    private void OnSnapshotChanged(object? sender, ForegroundSessionSnapshot snapshot)
    {
        if (_disposed || Snapshot is not { } current)
        {
            return;
        }

        Snapshot = new DiagnosticsSnapshot(_role, snapshot, current.TrustedDevices);
        ClearError();
        RaiseChanged();
    }

    private void SetError(Error error)
    {
        _errorCode = error.Code;
        _errorMessage = error.Message;
    }

    private void ClearError()
    {
        _errorCode = null;
        _errorMessage = null;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
