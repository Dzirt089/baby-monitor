using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Доверенное устройство: только публичная identity, fingerprint, роль и время сопряжения.
/// Не содержит ключей, токенов и персональных данных.
/// </summary>
public sealed record PairedDevice
{
    /// <summary>Создаёт доверенное устройство.</summary>
    /// <param name="peerIdentity">Публичная identity peer.</param>
    /// <param name="localRole">Роль локального устройства.</param>
    /// <param name="pairedAt">Время сопряжения.</param>
    /// <exception cref="ArgumentException">Некорректная роль или время.</exception>
    public PairedDevice(DeviceIdentityPublicKey peerIdentity, DeviceRole localRole, DateTimeOffset pairedAt)
    {
        ArgumentNullException.ThrowIfNull(peerIdentity);
        if (localRole is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль локального устройства должна быть Child или Parent.", nameof(localRole));
        }

        if (pairedAt == default)
        {
            throw new ArgumentException("Время сопряжения должно быть задано.", nameof(pairedAt));
        }

        PeerIdentity = peerIdentity;
        LocalRole = localRole;
        PeerRole = localRole is DeviceRole.Child ? DeviceRole.Parent : DeviceRole.Child;
        PairedAt = pairedAt;
    }

    /// <summary>Публичная identity peer.</summary>
    public DeviceIdentityPublicKey PeerIdentity { get; }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole LocalRole { get; }

    /// <summary>Роль peer (дополняет локальную).</summary>
    public DeviceRole PeerRole { get; }

    /// <summary>Время сопряжения.</summary>
    public DateTimeOffset PairedAt { get; }
}
