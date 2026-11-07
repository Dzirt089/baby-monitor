using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Security;

/// <summary>
/// Хранилище долгосрочной identity и операция подписи. Реализация — Android Keystore
/// (private key non-exportable); Application видит только публичную identity и подпись.
/// </summary>
public interface IPairingKeyStore
{
    /// <summary>Возвращает существующую или создаёт новую identity устройства.</summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Публичная identity либо типизированная ошибка.</returns>
    Task<Result<DeviceIdentityPublicKey>> GetOrCreateIdentityAsync(CancellationToken cancellationToken);

    /// <summary>Подписывает данные private key identity (ECDSA P-256, SHA-256).</summary>
    /// <param name="data">Данные для подписи.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Подпись либо типизированная ошибка.</returns>
    Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken);
}
