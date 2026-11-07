using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>Проверка contract-набора на ручном test double advertise.</summary>
public sealed class FakePeerAdvertiserContractTests : PeerAdvertisementContractTests
{
    protected override IPeerAdvertiser CreateAdvertiser() => new FakePeerAdvertiser();

    protected override IPeerAdvertiser CreateFailingAdvertiser() => new FakePeerAdvertiser(failStart: true);

    [Fact]
    public async Task StartAsync_сохраняет_опубликованное_advertisement()
    {
        var advertiser = new FakePeerAdvertiser();
        var advertisement = new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

        var result = await advertiser.StartAsync(advertisement, CancellationToken.None);

        Assert.True(result.IsSuccess);
        await using var session = result.Value;
        Assert.Equal(advertisement, advertiser.LastAdvertisement);
        await session.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Fail_переводит_advertise_сессию_в_Failed_и_сохраняет_LastError()
    {
        var advertiser = new FakePeerAdvertiser();
        var session = (FakePeerAdvertiserSession)(await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None)).Value;
        var failure = new Error(DiscoveryErrorCodes.NsdUnavailable, "NSD завершился ошибкой.");

        session.Fail(failure);

        Assert.Equal(PeerAdvertisementState.Failed, session.State);
        Assert.Same(failure, session.LastError);
    }
}
