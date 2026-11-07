using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Discovery;

/// <summary>Контракт advertisement: только Child и валидная версия протокола.</summary>
public sealed class PeerAdvertisementTests
{
    [Fact]
    public void Ребёнок_может_объявлять_сервис_с_валидной_версией()
    {
        var advertisement = new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

        Assert.Equal(DeviceRole.Child, advertisement.Role);
        Assert.Equal(PeerDiscoveryConstants.CurrentProtocolVersion, advertisement.ProtocolVersion);
    }

    [Theory]
    [InlineData(DeviceRole.Parent)]
    [InlineData(DeviceRole.Unknown)]
    public void Родитель_и_неизвестная_роль_не_могут_объявлять_сервис(DeviceRole role)
    {
        var exception = Assert.Throws<ArgumentException>(() => new PeerAdvertisement(role, 1));

        Assert.Equal("role", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Некорректная_версия_протокола_бросает_ArgumentOutOfRangeException(int version)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new PeerAdvertisement(DeviceRole.Child, version));

        Assert.Equal("protocolVersion", exception.ParamName);
    }
}
