using Android.Content;
using Android.Net;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Discovery;

/// <summary>
/// Проверка наличия локальной сети (Wi-Fi/Ethernet) до запуска NSD. Позволяет вернуть
/// типизированную ошибку discovery.wifi_unavailable вместо неинформативного NSD failure.
/// </summary>
internal static class ConnectivityPrecondition
{
    public static Error? CheckLocalNetworkAvailable(Context context)
    {
        var connectivity = context.ApplicationContext?.GetSystemService(Context.ConnectivityService) as ConnectivityManager;
        var network = connectivity?.ActiveNetwork;
        if (connectivity is null || network is null)
        {
            return new Error(DiscoveryErrorCodes.WifiUnavailable, "Нет активного сетевого подключения.");
        }

        var capabilities = connectivity.GetNetworkCapabilities(network);
        if (capabilities is null)
        {
            return new Error(DiscoveryErrorCodes.WifiUnavailable, "Не удалось определить возможности сети.");
        }

        if (capabilities.HasTransport(TransportType.Wifi) || capabilities.HasTransport(TransportType.Ethernet))
        {
            return null;
        }

        return new Error(DiscoveryErrorCodes.WifiUnavailable, "Локальная сеть Wi-Fi/Ethernet недоступна.");
    }
}
