using System.Net;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;
using BabyMonitor.Infrastructure.Tests.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Пара тестовых устройств Parent/Child с in-memory Keystore/trusted store и loopback-настройками
/// media: RTP привязан к 127.0.0.1, signaling TCP — к loopback.
/// </summary>
internal sealed class MediaTestPeers : IDisposable
{
    public MediaTestPeers(bool trustEachOther = true)
    {
        if (trustEachOther)
        {
            ParentTrustedStore.Trust(ChildKeyStore, DeviceRole.Parent);
            ChildTrustedStore.Trust(ParentKeyStore, DeviceRole.Child);
        }
    }

    public FakePairingKeyStore ParentKeyStore { get; } = new();

    public FakePairingKeyStore ChildKeyStore { get; } = new();

    public InMemoryTrustedDeviceStore ParentTrustedStore { get; } = new();

    public InMemoryTrustedDeviceStore ChildTrustedStore { get; } = new();

    public SipsorceryTransportOptions Options { get; } = new(
        mediaConnectTimeout: TimeSpan.FromSeconds(20),
        sendQueueCapacity: 8,
        receiveQueueCapacity: 4,
        rtpBindAddress: IPAddress.Loopback,
        signalingOptions: new SignalingOptions(TimeSpan.FromSeconds(10)));

    public SignalingIdentity CreateParentIdentity() =>
        new(ParentKeyStore, ParentTrustedStore, ChildKeyStore.FingerprintSha256);

    public SignalingIdentity CreateChildIdentity() =>
        new(ChildKeyStore, ChildTrustedStore, ParentKeyStore.FingerprintSha256);

    public static async Task<MediaLoopback> ConnectAsync(MediaTestPeers peers, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peers);
        var listener = new SipsorceryRealtimeTransportListener(peers.CreateChildIdentity(), peers.Options);
        var transport = new SipsorceryRealtimeTransport(peers.CreateParentIdentity(), peers.Options);

        var startResult = await listener.StartAsync(0, cancellationToken);
        if (!startResult.IsSuccess)
        {
            throw new InvalidOperationException("Не удалось запустить media listener: " + startResult.Error!.Code);
        }

        var listenerSession = startResult.Value;
        try
        {
            var parentTask = transport.ConnectAsync(
                new TransportEndpoint("127.0.0.1", listenerSession.BoundPort),
                cancellationToken);
            var childTask = listenerSession.AcceptAsync(cancellationToken);
            var parentResult = await parentTask;
            var childResult = await childTask;
            return new MediaLoopback(listenerSession, parentResult, childResult);
        }
        catch
        {
            await listenerSession.DisposeAsync();
            throw;
        }
    }

    public void Dispose()
    {
        ParentKeyStore.Dispose();
        ChildKeyStore.Dispose();
    }
}

/// <summary>Результаты loopback-подключения media: обе стороны и listener.</summary>
internal sealed class MediaLoopback : IAsyncDisposable
{
    private readonly IRealtimeTransportListenerSession _listenerSession;

    public MediaLoopback(
        IRealtimeTransportListenerSession listenerSession,
        Result<IRealtimeTransportSession> parentResult,
        Result<IRealtimeTransportSession> childResult)
    {
        _listenerSession = listenerSession;
        ParentResult = parentResult;
        ChildResult = childResult;
    }

    public Result<IRealtimeTransportSession> ParentResult { get; }

    public Result<IRealtimeTransportSession> ChildResult { get; }

    public async ValueTask DisposeAsync()
    {
        if (ParentResult.IsSuccess)
        {
            await ParentResult.Value.DisposeAsync();
        }

        if (ChildResult.IsSuccess)
        {
            await ChildResult.Value.DisposeAsync();
        }

        await _listenerSession.DisposeAsync();
    }
}
