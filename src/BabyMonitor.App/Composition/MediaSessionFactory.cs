using Android.Content;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Media;
using BabyMonitor.Application.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Infrastructure.Audio;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;
using BabyMonitor.Platform.Android.Audio;
using BabyMonitor.Platform.Android.Connectivity;
using BabyMonitor.Platform.Android.Discovery;
using BabyMonitor.Platform.Android.Permissions;
using BabyMonitor.Platform.Android.Security;

namespace BabyMonitor.App.Composition;

/// <summary>
/// Composition root media-сеанса (005 phase 2): по trusted store находит доверенного peer для роли,
/// собирает signaling identity с pinning-ом ожидаемого fingerprint, SIPSorcery transport/listener,
/// Android audio adapters, Opus codec, discovery и connectivity — и возвращает
/// <see cref="MonitoringMediaSession"/>. Вызывается из foreground service через
/// <see cref="IMonitoringMediaSessionFactoryProvider"/>; Activity не собирает media-зависимости.
/// </summary>
internal sealed class MediaSessionFactory : IMonitoringMediaSessionFactory
{
    private readonly Context _context;
    private readonly IPairingKeyStore _keyStore = new AndroidKeystorePairingKeyStore();
    private readonly AndroidTrustedDeviceStore _trustedStore;
    private readonly TimeProvider _timeProvider = TimeProvider.System;

    /// <summary>Создаёт фабрику media-сеансов приложения.</summary>
    /// <param name="context">Android context (Application; Activity не удерживается).</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен null.</exception>
    public MediaSessionFactory(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.ApplicationContext ?? context;
        _trustedStore = new AndroidTrustedDeviceStore(_context);
    }

    /// <inheritdoc />
    public async Task<Result<MonitoringMediaSession>> CreateAsync(
        DeviceRole role,
        CancellationToken cancellationToken)
    {
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            return Result<MonitoringMediaSession>.Failure(new Error(
                MediaSessionErrorCodes.InvalidState,
                "Media-сеанс требует роль Child или Parent."));
        }

        var devices = await _trustedStore.GetAllAsync(cancellationToken).ConfigureAwait(false);
        if (!devices.IsSuccess)
        {
            return Result<MonitoringMediaSession>.Failure(new Error(
                MediaSessionErrorCodes.TrustedStoreFailed,
                "Не удалось прочитать список доверенных устройств."));
        }

        var peer = devices.Value
            .Where(device => device.LocalRole == role)
            .OrderByDescending(device => device.PairedAt)
            .FirstOrDefault();
        if (peer is null)
        {
            return Result<MonitoringMediaSession>.Failure(new Error(
                MediaSessionErrorCodes.NoTrustedPeer,
                "Сначала сопрягите два устройства через QR-код."));
        }

        var peerFingerprint = peer.PeerIdentity.FingerprintSha256;
        var localNetworkPermission = new ContextLocalNetworkPermission(_context);
        var discovery = new NsdPeerDiscovery(_context, localNetworkPermission);
        var advertiser = new NsdPeerAdvertiser(_context, localNetworkPermission);
        var signalingIdentity = new SignalingIdentity(_keyStore, _trustedStore, peerFingerprint);
        var transportOptions = new SipsorceryTransportOptions(
            signalingOptions: new SignalingOptions(timeProvider: _timeProvider));
        var transport = new SipsorceryRealtimeTransport(signalingIdentity, transportOptions);
        var listener = new SipsorceryRealtimeTransportListener(signalingIdentity, transportOptions);
        var capture = new AndroidAudioCapture(_context);
        var playback = new AndroidAudioPlayback(_context);
        var codec = new ConcentusAudioCodec();
        var connectivity = new AndroidConnectivityMonitor(_context);
        var coordinator = new MonitoringMediaCoordinator(
            role,
            discovery,
            advertiser,
            transport,
            listener,
            capture,
            playback,
            codec,
            connectivity,
            new ReconnectPolicy(),
            _timeProvider,
            peerFingerprint);

        return Result<MonitoringMediaSession>.Success(new MonitoringMediaSession(coordinator, connectivity));
    }
}
