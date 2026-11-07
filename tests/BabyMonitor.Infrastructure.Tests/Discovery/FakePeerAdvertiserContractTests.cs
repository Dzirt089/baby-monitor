using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>Проверка contract-набора на ручном test double advertise.</summary>
public sealed class FakePeerAdvertiserContractTests : PeerAdvertisementContractTests
{
    protected override IPeerAdvertiser CreateAdvertiser() => new FakePeerAdvertiser();

    protected override IPeerAdvertiser CreateFailingAdvertiser() => new FakePeerAdvertiser(failStart: true);

    [Fact]
    public async Task StartAsync_сохраняет_advertisement_и_TXT_содержит_только_разрешённые_ключи()
    {
        var advertiser = new FakePeerAdvertiser();
        var advertisement = new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

        var result = await advertiser.StartAsync(advertisement, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var session = result.Value;
        Assert.Equal(advertisement, advertiser.LastAdvertisement);
        var txt = PeerTxtRecords.Create(advertisement);
        Assert.Equal(2, txt.Count);
        Assert.True(txt.ContainsKey(PeerDiscoveryConstants.TxtProtocolVersionKey));
        Assert.True(txt.ContainsKey(PeerDiscoveryConstants.TxtRoleKey));
        await session.StopAsync(CancellationToken.None);
    }
}
