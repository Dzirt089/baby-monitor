namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>Стабильные коды ошибок discovery/NSD для application-слоя.</summary>
public static class DiscoveryErrorCodes
{
    /// <summary>Wi-Fi отсутствует или выключен.</summary>
    public const string WifiUnavailable = "discovery.wifi_unavailable";

    /// <summary>Сервис не найден за отведённое время.</summary>
    public const string ServiceNotFound = "discovery.service_not_found";

    /// <summary>NSD resolve не выполнен.</summary>
    public const string ResolveFailed = "discovery.resolve_failed";

    /// <summary>Нет разрешения на локальную сеть/NSD.</summary>
    public const string PermissionDenied = "discovery.permission_denied";

    /// <summary>NSD недоступен на устройстве.</summary>
    public const string NsdUnavailable = "discovery.nsd_unavailable";

    /// <summary>Некорректные TXT-записи peer.</summary>
    public const string InvalidTxt = "discovery.invalid_txt";
}
