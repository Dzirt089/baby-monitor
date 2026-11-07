using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.Permissions;
using BabyMonitor.Platform.Android.Qr;
using BabyMonitor.Platform.Android.Security;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Composition root экрана сопряжения (per-Activity scope): собирает Android-адаптеры
/// (Keystore identity, CSPRNG entropy, trusted store, CAMERA permission, QR camera scanner) и
/// Application screen models. Activity не создаёт адаптеры напрямую (AGENTS.md, раздел 5).
/// </summary>
internal sealed class PairingComposition : IDisposable
{
    private readonly IPairingKeyStore _keyStore;
    private readonly IPairingEntropy _entropy;
    private readonly ITrustedDeviceStore _trustedStore;
    private readonly TimeProvider _timeProvider;
    private int _disposed;

    /// <summary>Создаёт composition root для указанной роли.</summary>
    /// <param name="activity">Activity экрана сопряжения (permission host + camera lifecycle owner).</param>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <exception cref="ArgumentException">Роль Unknown.</exception>
    public PairingComposition(PermissionActivity activity, DeviceRole role)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль должна быть Child или Parent.", nameof(role));
        }

        Role = role;
        _timeProvider = TimeProvider.System;
        _keyStore = new AndroidKeystorePairingKeyStore();
        _entropy = new AndroidSecureEntropy();
        _trustedStore = new AndroidTrustedDeviceStore(activity);
        CameraPermission = new AndroidCameraPermission(activity, activity);
        Scanner = new QrCameraScanner(activity);
        TrustedModel = new TrustedDevicesScreenModel(_trustedStore);
    }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole Role { get; }

    /// <summary>Адаптер runtime-разрешения CAMERA.</summary>
    public AndroidCameraPermission CameraPermission { get; }

    /// <summary>Сканер QR с камеры (Camera2).</summary>
    public QrCameraScanner Scanner { get; }

    /// <summary>Модель доверенных устройств.</summary>
    public TrustedDevicesScreenModel TrustedModel { get; }

    /// <summary>Создаёт Model экрана Child.</summary>
    public ChildPairingScreenModel CreateChildModel() =>
        new(_keyStore, _entropy, _trustedStore, _timeProvider);

    /// <summary>Создаёт Model экрана Parent.</summary>
    public ParentPairingScreenModel CreateParentModel() =>
        new(_keyStore, _trustedStore, _timeProvider);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Scanner.Dispose();
    }
}
