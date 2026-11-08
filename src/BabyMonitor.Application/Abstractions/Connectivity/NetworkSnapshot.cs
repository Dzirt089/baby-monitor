namespace BabyMonitor.Application.Abstractions.Connectivity;

/// <summary>
/// Снимок сетевого состояния, достаточный для reconnect-решений: есть ли LAN-подключение под
/// мониторинг. Не содержит IP/MAC/SSID (privacy) и не заменяет discovery.
/// </summary>
public sealed record NetworkSnapshot
{
    /// <summary>Создаёт снимок.</summary>
    /// <param name="hasLanNetwork">Есть активное LAN-подключение (Wi-Fi/Ethernet).</param>
    public NetworkSnapshot(bool hasLanNetwork) => HasLanNetwork = hasLanNetwork;

    /// <summary>Есть активное LAN-подключение.</summary>
    public bool HasLanNetwork { get; }
}
