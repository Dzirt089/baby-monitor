namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>
/// Константы discovery-протокола. Единый источник истины для service type и TXT-ключей;
/// Android-адаптер и тесты используют только эти значения.
/// </summary>
public static class PeerDiscoveryConstants
{
    /// <summary>mDNS service type детского устройства.</summary>
    public const string ServiceType = "_babymonitor._tcp";

    /// <summary>TXT-ключ protocol version.</summary>
    public const string TxtProtocolVersionKey = "v";

    /// <summary>TXT-ключ роли устройства.</summary>
    public const string TxtRoleKey = "role";

    /// <summary>Текущая версия discovery-протокола.</summary>
    public const int CurrentProtocolVersion = 1;
}
