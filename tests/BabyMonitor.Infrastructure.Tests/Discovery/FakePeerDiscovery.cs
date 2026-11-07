using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>
/// Ручной test double IPeerDiscovery/IPeerAdvertiser для contract tests. Не production-адаптер:
/// хранит peer-ов/объявления в памяти, не использует сеть и NSD.
/// </summary>
internal sealed class FakePeerDiscovery : IPeerDiscovery
{
    private readonly bool _failStart;
    private readonly IReadOnlyList<DiscoveredPeer> _peers;

    public FakePeerDiscovery(bool failStart = false, IReadOnlyList<DiscoveredPeer>? peers = null)
    {
        _failStart = failStart;
        _peers = peers ?? [];
    }

    public Task<Result<IPeerDiscoverySession>> StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<IPeerDiscoverySession>>(cancellationToken);
        }

        if (_failStart)
        {
            return Task.FromResult(Result<IPeerDiscoverySession>.Failure(
                new Error(DiscoveryErrorCodes.WifiUnavailable, "Wi-Fi недоступен.")));
        }

        var session = new FakePeerDiscoverySession(_peers);
        session.MarkDiscovering();
        return Task.FromResult(Result<IPeerDiscoverySession>.Success(session));
    }
}

/// <summary>Discovery-сессия test double: bounded очередь peer-ов и события состояния.</summary>
internal sealed class FakePeerDiscoverySession : IPeerDiscoverySession
{
    private readonly Channel<DiscoveredPeer> _peers;
    private int _state = (int)PeerDiscoveryState.Starting;

    public FakePeerDiscoverySession(IReadOnlyList<DiscoveredPeer> peers)
    {
        _peers = Channel.CreateBounded<DiscoveredPeer>(
            new BoundedChannelOptions(8)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

        foreach (var peer in peers)
        {
            _peers.Writer.TryWrite(peer);
        }
    }

    public PeerDiscoveryState State => (PeerDiscoveryState)Volatile.Read(ref _state);

    public Error? LastError { get; private set; }

    public event EventHandler<PeerDiscoveryStateChangedEventArgs>? StateChanged;

    public void MarkDiscovering()
    {
        Volatile.Write(ref _state, (int)PeerDiscoveryState.Discovering);
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Discovering));
    }

    public async IAsyncEnumerable<DiscoveredPeer> DiscoverPeersAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var peer in _peers.Reader.ReadAllAsync(cancellationToken))
        {
            yield return peer;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (State is PeerDiscoveryState.Stopped)
        {
            return Task.CompletedTask;
        }

        Volatile.Write(ref _state, (int)PeerDiscoveryState.Stopped);
        _peers.Writer.TryComplete();
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Stopped));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}

/// <summary>Ручной test double IPeerAdvertiser; сохраняет опубликованное advertisement.</summary>
internal sealed class FakePeerAdvertiser : IPeerAdvertiser
{
    private readonly bool _failStart;

    public FakePeerAdvertiser(bool failStart = false)
    {
        _failStart = failStart;
    }

    public PeerAdvertisement? LastAdvertisement { get; private set; }

    public Task<Result<IPeerAdvertiserSession>> StartAsync(PeerAdvertisement advertisement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advertisement);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<IPeerAdvertiserSession>>(cancellationToken);
        }

        if (_failStart)
        {
            return Task.FromResult(Result<IPeerAdvertiserSession>.Failure(
                new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD недоступен.")));
        }

        LastAdvertisement = advertisement;
        var session = new FakePeerAdvertiserSession(advertisement);
        session.MarkAdvertising();
        return Task.FromResult(Result<IPeerAdvertiserSession>.Success(session));
    }
}

/// <summary>Advertise-сессия test double.</summary>
internal sealed class FakePeerAdvertiserSession : IPeerAdvertiserSession
{
    private int _state = (int)PeerAdvertisementState.Starting;

    public FakePeerAdvertiserSession(PeerAdvertisement advertisement)
    {
        Advertisement = advertisement;
    }

    public PeerAdvertisement Advertisement { get; }

    public PeerAdvertisementState State => (PeerAdvertisementState)Volatile.Read(ref _state);

    public Error? LastError { get; private set; }

    public event EventHandler<PeerAdvertisementStateChangedEventArgs>? StateChanged;

    public void MarkAdvertising()
    {
        Volatile.Write(ref _state, (int)PeerAdvertisementState.Advertising);
        StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Advertising));
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (State is PeerAdvertisementState.Stopped)
        {
            return Task.CompletedTask;
        }

        Volatile.Write(ref _state, (int)PeerAdvertisementState.Stopped);
        StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Stopped));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(CancellationToken.None);
    }
}
