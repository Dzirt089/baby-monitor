using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Presentation-agnostic модель экрана доверенных устройств: читает список из
/// <see cref="ITrustedDeviceStore"/>, фильтрует по локальной роли и позволяет забыть устройство.
/// Наружу отдаёт только read-only копию представлений без приватных данных.
/// </summary>
public sealed class TrustedDevicesScreenModel
{
    private readonly ITrustedDeviceStore _store;
    private IReadOnlyList<PairedDeviceSummary> _devices = Array.AsReadOnly(Array.Empty<PairedDeviceSummary>());
    private bool _isBusy;
    private string? _errorCode;
    private string? _errorMessage;

    /// <summary>Создаёт модель экрана доверенных устройств.</summary>
    /// <param name="store">Хранилище доверенных устройств.</param>
    /// <exception cref="ArgumentNullException"><paramref name="store"/> равен <see langword="null"/>.</exception>
    public TrustedDevicesScreenModel(ITrustedDeviceStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>Доверенные устройства для текущей локальной роли, отсортированные по времени сопряжения (новые первыми).</summary>
    public IReadOnlyList<PairedDeviceSummary> Devices => _devices;

    /// <summary>Идёт асинхронная операция.</summary>
    public bool IsBusy => _isBusy;

    /// <summary>Стабильный код последней ошибки (без секретов); <see langword="null"/> при успехе.</summary>
    public string? ErrorCode => _errorCode;

    /// <summary>Безопасное для показа сообщение последней ошибки; <see langword="null"/> при успехе.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>Есть ли хотя бы одно доверенное устройство для выбранной роли.</summary>
    public bool HasTrustedPeer => _devices.Count > 0;

    /// <summary>Уведомление об изменении наблюдаемого состояния.</summary>
    public event EventHandler? Changed;

    /// <summary>Перечитывает доверенные устройства для указанной локальной роли.</summary>
    /// <param name="localRole">Роль локального устройства: Child или Parent.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">Роль не Child и не Parent.</exception>
    /// <remarks>
    /// При ошибке хранилища список не затирается: UI продолжает показывать последние успешные
    /// данные, а ошибка доступна через <see cref="ErrorCode"/>.
    /// </remarks>
    public async Task RefreshAsync(DeviceRole localRole, CancellationToken cancellationToken)
    {
        ValidateRole(localRole);
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        RaiseChanged();
        try
        {
            var result = await _store.GetAllAsync(cancellationToken);
            if (result.IsSuccess)
            {
                var summaries = result.Value
                    .Where(device => device.LocalRole == localRole)
                    .OrderByDescending(device => device.PairedAt)
                    .Select(PairedDeviceSummary.From)
                    .ToArray();
                _devices = Array.AsReadOnly(summaries);
                ClearError();
            }
            else
            {
                SetError(result.Error!);
            }
        }
        finally
        {
            _isBusy = false;
            RaiseChanged();
        }
    }

    /// <summary>Удаляет доверенное устройство (ForgetPairedDevice) и обновляет список.</summary>
    /// <param name="fingerprintSha256">Полный hex fingerprint устройства.</param>
    /// <param name="localRole">Роль локального устройства: Child или Parent.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <exception cref="ArgumentException">Пустой fingerprint или роль не Child и не Parent.</exception>
    public async Task ForgetAsync(string fingerprintSha256, DeviceRole localRole, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);
        ValidateRole(localRole);
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        RaiseChanged();
        try
        {
            var result = await _store.RemoveAsync(fingerprintSha256, cancellationToken);
            if (result.IsSuccess)
            {
                var remaining = _devices
                    .Where(device => !string.Equals(
                        device.FingerprintSha256,
                        fingerprintSha256,
                        StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                _devices = Array.AsReadOnly(remaining);
                ClearError();
            }
            else
            {
                SetError(result.Error!);
            }
        }
        finally
        {
            _isBusy = false;
            RaiseChanged();
        }
    }

    private static void ValidateRole(DeviceRole localRole)
    {
        if (localRole is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль локального устройства должна быть Child или Parent.", nameof(localRole));
        }
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
