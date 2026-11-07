using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Обнаруженный peer. Идентификатор discovery не является постоянной identity устройства:
/// он действителен в рамках сессии/сети и не сохраняется как endpoint или identity.
/// Доверие к peer устанавливается только pairing-слоем (feature 004), не по mDNS/IP.
/// </summary>
public sealed record DiscoveredPeer
{
    /// <summary>Создаёт описание обнаруженного peer.</summary>
    /// <param name="discoveryId">Opaque id в рамках discovery-сессии.</param>
    /// <param name="endpoint">Endpoint для transport-соединения.</param>
    /// <param name="role">Роль удалённого устройства.</param>
    /// <param name="protocolVersion">Версия discovery-протокола peer.</param>
    /// <exception cref="ArgumentException">Пустой discovery id или роль не Child.</exception>
    /// <exception cref="ArgumentNullException">Endpoint не задан.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Некорректная версия протокола.</exception>
    public DiscoveredPeer(string discoveryId, TransportEndpoint endpoint, DeviceRole role, int protocolVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discoveryId);
        ArgumentNullException.ThrowIfNull(endpoint);

        if (role is not DeviceRole.Child)
        {
            throw new ArgumentException("В MVP discovery находит только детское устройство (Child).", nameof(role));
        }

        if (protocolVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(protocolVersion), protocolVersion, "Версия протокола должна быть больше нуля.");
        }

        DiscoveryId = discoveryId;
        Endpoint = endpoint;
        Role = role;
        ProtocolVersion = protocolVersion;
    }

    /// <summary>Opaque идентификатор peer в рамках discovery-сессии (не identity).</summary>
    public string DiscoveryId { get; }

    /// <summary>Endpoint для transport-соединения.</summary>
    public TransportEndpoint Endpoint { get; }

    /// <summary>Роль удалённого устройства.</summary>
    public DeviceRole Role { get; }

    /// <summary>Версия discovery-протокола peer.</summary>
    public int ProtocolVersion { get; }
}
