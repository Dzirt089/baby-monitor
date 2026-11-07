namespace BabyMonitor.Platform.Android.Discovery;

/// <summary>
/// Имена mDNS-сервисов. Instance name случайный в рамках сессии и не является identity
/// устройства; IP/mDNS не дают доверия peer (доверие — только pairing, feature 004).
/// </summary>
internal static class NsdServiceNames
{
    public static string CreateInstanceName() => "babymonitor-child-" + Guid.NewGuid().ToString("N");
}
