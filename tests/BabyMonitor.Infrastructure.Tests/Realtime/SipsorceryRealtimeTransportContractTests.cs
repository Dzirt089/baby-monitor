using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Realtime;
using BabyMonitor.Infrastructure.Signaling;
using BabyMonitor.Infrastructure.Tests.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Общий contract suite <see cref="RealtimeTransportContractTests"/> для реального
/// SIPSorcery-адаптера (ADR-0005/FR-015): каждая фабрика поднимает loopback-пару
/// Child listener + Parent transport на 127.0.0.1. Сессия-обёртка освобождает обе стороны,
/// listener и тестовые ключи при закрытии, поэтому suite не оставляет сокетов и задач.
/// </summary>
public sealed class SipsorceryRealtimeTransportContractTests : RealtimeTransportContractTests
{
    private static readonly FakePairingKeyStore FailingKeys = new();
    private static readonly InMemoryTrustedDeviceStore FailingStore = new();

    /// <inheritdoc />
    protected override IRealtimeTransport CreateTransport() => new LoopbackContractTransport();

    /// <inheritdoc />
    protected override IRealtimeTransport CreateFailingTransport()
    {
        // Доверие peer отсутствует: отказ возвращается до открытия сокета (без сетевого таймаута).
        var identity = new SignalingIdentity(FailingKeys, FailingStore, new string('0', 64));
        return new SipsorceryRealtimeTransport(identity, LoopbackOptions());
    }

    private static SipsorceryTransportOptions LoopbackOptions() => new(
        mediaConnectTimeout: TimeSpan.FromSeconds(20),
        sendQueueCapacity: 8,
        receiveQueueCapacity: 4,
        rtpBindAddress: System.Net.IPAddress.Loopback,
        signalingOptions: new SignalingOptions(TimeSpan.FromSeconds(10)));
}

/// <summary>Транспорт, который для каждого ConnectAsync поднимает свежую loopback-пару peer-ов.</summary>
internal sealed class LoopbackContractTransport : IRealtimeTransport
{
    /// <inheritdoc />
    public async Task<Result<IRealtimeTransportSession>> ConnectAsync(
        TransportEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        cancellationToken.ThrowIfCancellationRequested();

        var peers = new MediaTestPeers();
        var listener = new SipsorceryRealtimeTransportListener(peers.CreateChildIdentity(), peers.Options);
        var transport = new SipsorceryRealtimeTransport(peers.CreateParentIdentity(), peers.Options);

        var listenerResult = await listener.StartAsync(0, cancellationToken);
        if (!listenerResult.IsSuccess)
        {
            peers.Dispose();
            return Result<IRealtimeTransportSession>.Failure(listenerResult.Error!);
        }

        var listenerSession = listenerResult.Value;
        try
        {
            var parentTask = transport.ConnectAsync(
                new TransportEndpoint("127.0.0.1", listenerSession.BoundPort),
                cancellationToken);
            var childTask = listenerSession.AcceptAsync(cancellationToken);
            var parentResult = await parentTask;
            var childResult = await childTask;

            if (!parentResult.IsSuccess || !childResult.IsSuccess)
            {
                if (parentResult.IsSuccess)
                {
                    await parentResult.Value.DisposeAsync();
                }

                if (childResult.IsSuccess)
                {
                    await childResult.Value.DisposeAsync();
                }

                await listenerSession.DisposeAsync();
                peers.Dispose();

                // Loopback-пара должна подниматься в тестовом окружении; отказ — дефект harness-а,
                // а не проверяемого контракта, поэтому сообщаем коды для диагностики.
                throw new InvalidOperationException(
                    "Loopback media harness не поднял сессию: parent=" +
                    (parentResult.IsSuccess ? "ok" : parentResult.Error!.Code) +
                    ", child=" +
                    (childResult.IsSuccess ? "ok" : childResult.Error!.Code));
            }

            return Result<IRealtimeTransportSession>.Success(
                new LoopbackContractSession(parentResult.Value, childResult.Value, listenerSession, peers));
        }
        catch
        {
            await listenerSession.DisposeAsync();
            peers.Dispose();
            throw;
        }
    }
}

/// <summary>
/// Сессия-обёртка contract suite: делегирует parent-стороне и освобождает child-сессию,
/// listener и тестовые ключи при закрытии/dispose.
/// </summary>
internal sealed class LoopbackContractSession : IRealtimeTransportSession
{
    private readonly IRealtimeTransportSession _parentSession;
    private readonly IRealtimeTransportSession _childSession;
    private readonly IRealtimeTransportListenerSession _listenerSession;
    private readonly MediaTestPeers _peers;
    private int _released;

    public LoopbackContractSession(
        IRealtimeTransportSession parentSession,
        IRealtimeTransportSession childSession,
        IRealtimeTransportListenerSession listenerSession,
        MediaTestPeers peers)
    {
        _parentSession = parentSession;
        _childSession = childSession;
        _listenerSession = listenerSession;
        _peers = peers;
    }

    /// <inheritdoc />
    public RealtimeTransportState State => _parentSession.State;

    /// <inheritdoc />
    public Error? LastError => _parentSession.LastError;

    /// <inheritdoc />
    public event EventHandler<RealtimeTransportStateChangedEventArgs>? StateChanged
    {
        add => _parentSession.StateChanged += value;
        remove => _parentSession.StateChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler? HeartbeatReceived
    {
        add => _parentSession.HeartbeatReceived += value;
        remove => _parentSession.HeartbeatReceived -= value;
    }

    /// <inheritdoc />
    public ValueTask SendAudioAsync(ReadOnlyMemory<byte> encodedAudioFrame, CancellationToken cancellationToken) =>
        _parentSession.SendAudioAsync(encodedAudioFrame, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ReceivedAudioFrame> ReceiveAudioAsync(CancellationToken cancellationToken) =>
        _parentSession.ReceiveAudioAsync(cancellationToken);

    /// <inheritdoc />
    public async Task CloseAsync(CancellationToken cancellationToken)
    {
        await _parentSession.CloseAsync(cancellationToken);
        await ReleaseOwnedResourcesAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }

    private async Task ReleaseOwnedResourcesAsync()
    {
        if (Interlocked.Exchange(ref _released, 1) == 1)
        {
            return;
        }

        await _childSession.DisposeAsync();
        await _listenerSession.DisposeAsync();
        _peers.Dispose();
    }
}
