using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Discovery;

/// <summary>Контракт DiscoveredPeer: opaque id, валидный endpoint, только Child.</summary>
public sealed class DiscoveredPeerTests
{
    private static TransportEndpoint Endpoint => new("192.0.2.10", 50000);

    [Fact]
    public void Валидный_peer_сохраняет_значения()
    {
        var peer = new DiscoveredPeer("peer-1", Endpoint, DeviceRole.Child, 1);

        Assert.Equal("peer-1", peer.DiscoveryId);
        Assert.Equal(Endpoint, peer.Endpoint);
        Assert.Equal(DeviceRole.Child, peer.Role);
        Assert.Equal(1, peer.ProtocolVersion);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Пустой_discovery_id_бросает_ArgumentException(string id)
    {
        Assert.Throws<ArgumentException>(() => new DiscoveredPeer(id, Endpoint, DeviceRole.Child, 1));
    }

    [Fact]
    public void Null_endpoint_бросает_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new DiscoveredPeer("peer-1", null!, DeviceRole.Child, 1));
    }

    [Fact]
    public void Родительская_роль_не_допустима_для_обнаруженного_peer()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new DiscoveredPeer("peer-1", Endpoint, DeviceRole.Parent, 1));

        Assert.Equal("role", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Некорректная_версия_протокола_бросает_ArgumentOutOfRangeException(int version)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiscoveredPeer("peer-1", Endpoint, DeviceRole.Child, version));
    }

    [Fact]
    public void Peer_является_record_с_равенством_по_полям()
    {
        var a = new DiscoveredPeer("peer-1", Endpoint, DeviceRole.Child, 1);
        var b = new DiscoveredPeer("peer-1", new TransportEndpoint("192.0.2.10", 50000), DeviceRole.Child, 1);
        var c = new DiscoveredPeer("peer-2", Endpoint, DeviceRole.Child, 1);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
