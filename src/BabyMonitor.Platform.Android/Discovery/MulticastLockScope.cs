using Android.Content;
using Android.Net.Wifi;

namespace BabyMonitor.Platform.Android.Discovery;

/// <summary>
/// Владение Wi-Fi multicast lock на время NSD-сессии: acquire при старте, release при Stop/Dispose.
/// Без multicast lock Android может не получать mDNS-ответы.
/// </summary>
internal sealed class MulticastLockScope : IDisposable
{
    private const string LockTag = "babymonitor-nsd";

    private readonly WifiManager.MulticastLock? _lock;

    public MulticastLockScope(Context context)
    {
        var wifiManager = context.ApplicationContext?.GetSystemService(Context.WifiService) as WifiManager;
        _lock = wifiManager?.CreateMulticastLock(LockTag);
        if (_lock is not null)
        {
            _lock.SetReferenceCounted(false);
            _lock.Acquire();
        }
    }

    public void Dispose()
    {
        if (_lock is null)
        {
            return;
        }

        if (_lock.IsHeld)
        {
            _lock.Release();
        }

        _lock.Dispose();
    }
}
