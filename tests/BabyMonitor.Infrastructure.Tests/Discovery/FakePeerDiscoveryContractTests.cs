using BabyMonitor.Application.Abstractions.Discovery;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>Проверка contract-набора на ручном test double discovery.</summary>
public sealed class FakePeerDiscoveryContractTests : PeerDiscoveryContractTests
{
    protected override IPeerDiscovery CreateDiscovery() => new FakePeerDiscovery();

    protected override IPeerDiscovery CreateFailingDiscovery() => new FakePeerDiscovery(failStart: true);

    [Fact]
    public async Task DiscoverPeersAsync_возвращает_подготовленных_peer_ов()
    {
        var expected = CreatePeer();
        var discovery = new FakePeerDiscovery(peers: [expected]);
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var peers = new List<DiscoveredPeer>();

        await using (var enumerator = session.DiscoverPeersAsync(cts.Token).GetAsyncEnumerator(cts.Token))
        {
            while (peers.Count < 1 && await enumerator.MoveNextAsync())
            {
                peers.Add(enumerator.Current);
            }
        }

        await session.StopAsync(CancellationToken.None);
        Assert.Single(peers);
        Assert.Equal(expected, peers[0]);
    }
}
