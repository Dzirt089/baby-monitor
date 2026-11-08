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

    /// <summary>
    /// Фиксированный порт signaling/media-сервиса Child, который публикуется через NSD и на котором
    /// listener принимает соединение (005 phase 2). Порт остаётся provisional: в Application-контракте
    /// <see cref="PeerAdvertisement"/> пока нет поля порта, поэтому advertiser публикует это значение,
    /// а координатор требует, чтобы listener занял именно его (иначе typed invalid_state). При
    /// добавлении transport port в advertisement перейти на динамический BoundPort listener-а.
    /// </summary>
    public const int ProvisionalTransportPort = 47800;
}
