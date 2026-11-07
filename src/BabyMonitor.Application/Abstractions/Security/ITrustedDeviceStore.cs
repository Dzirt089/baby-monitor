using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Security;

/// <summary>
/// Хранилище доверенных устройств. Содержит только публичные identity/fingerprint/роль/время;
/// секреты и private keys не сохраняются.
/// </summary>
public interface ITrustedDeviceStore
{
    /// <summary>Находит доверенное устройство по fingerprint.</summary>
    /// <param name="fingerprintSha256">Hex fingerprint.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Устройство или null.</returns>
    Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken);

    /// <summary>Возвращает все доверенные устройства.</summary>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Список (может быть пустым).</returns>
    Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Сохраняет/обновляет доверенное устройство.</summary>
    /// <param name="device">Доверенное устройство.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Успех либо store_failed.</returns>
    Task<Result> SaveAsync(PairedDevice device, CancellationToken cancellationToken);

    /// <summary>Удаляет доверенное устройство (ForgetPairedDevice).</summary>
    /// <param name="fingerprintSha256">Hex fingerprint.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Успех либо store_failed.</returns>
    Task<Result> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken);

    /// <summary>Проверяет, доверено ли устройство с данным fingerprint.</summary>
    /// <param name="fingerprintSha256">Hex fingerprint.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>true, если устройство в trusted store.</returns>
    Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken);
}
