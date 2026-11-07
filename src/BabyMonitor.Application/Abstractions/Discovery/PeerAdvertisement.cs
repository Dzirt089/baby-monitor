using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Данные, которые Child публикует в mDNS. Не содержит персональных данных, ключей и
/// pairing secrets (AGENTS.md раздел 8); только protocol version и роль.
/// </summary>
public sealed record PeerAdvertisement
{
    /// <summary>Создаёт объявление сервиса Child.</summary>
    /// <param name="role">Роль объявляющего устройства; в MVP объявлять может только Child.</param>
    /// <param name="protocolVersion">Версия discovery-протокола, больше нуля.</param>
    /// <exception cref="ArgumentException">Роль не Child.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Некорректная версия протокола.</exception>
    public PeerAdvertisement(DeviceRole role, int protocolVersion)
    {
        if (role is not DeviceRole.Child)
        {
            throw new ArgumentException("Сервис в mDNS публикует только детское устройство (Child).", nameof(role));
        }

        if (protocolVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), protocolVersion, "Версия протокола должна быть больше нуля.");
        }

        Role = role;
        ProtocolVersion = protocolVersion;
    }

    /// <summary>Роль объявляющего устройства (в MVP всегда Child).</summary>
    public DeviceRole Role { get; }

    /// <summary>Версия discovery-протокола.</summary>
    public int ProtocolVersion { get; }
}
