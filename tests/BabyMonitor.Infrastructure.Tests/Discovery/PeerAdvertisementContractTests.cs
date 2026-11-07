using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>
/// Общий набор contract tests для любой реализации IPeerAdvertiser (NSD advertise).
/// </summary>
public abstract class PeerAdvertisementContractTests
{
    protected abstract IPeerAdvertiser CreateAdvertiser();

    protected abstract IPeerAdvertiser CreateFailingAdvertiser();

    protected static PeerAdvertisement CreateAdvertisement() =>
        new(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

    [Fact]
    public async Task StartAsync_возвращает_сессию_в_состоянии_Advertising()
    {
        var advertiser = CreateAdvertiser();

        var result = await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None);

        Assert.True(result.IsSuccess, "Ожидался успешный Result advertise.");
        await using var session = result.Value;
        Assert.Equal(PeerAdvertisementState.Advertising, session.State);
    }

    [Fact]
    public async Task StartAsync_при_отказе_возвращает_типизированную_ошибку()
    {
        var advertiser = CreateFailingAdvertiser();

        var result = await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None);

        Assert.False(result.IsSuccess, "Отказ advertise должен возвращаться как Result.");
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task StartAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        var advertiser = CreateAdvertiser();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await advertiser.StartAsync(CreateAdvertisement(), cts.Token));
    }

    [Fact]
    public async Task StopAsync_переводит_сессию_в_Stopped_и_идемпотентен()
    {
        var advertiser = CreateAdvertiser();
        var session = (await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None)).Value;

        await session.StopAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Assert.Equal(PeerAdvertisementState.Stopped, session.State);
    }

    [Fact]
    public async Task StateChanged_уведомляет_о_Stopped_ровно_один_раз()
    {
        var advertiser = CreateAdvertiser();
        var session = (await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None)).Value;
        var stoppedEvents = 0;
        session.StateChanged += (_, args) =>
        {
            if (args.State is PeerAdvertisementState.Stopped)
            {
                stoppedEvents++;
            }
        };

        await session.StopAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Assert.Equal(1, stoppedEvents);
    }

    [Fact]
    public async Task DisposeAsync_останавливает_сессию()
    {
        var advertiser = CreateAdvertiser();
        var session = (await advertiser.StartAsync(CreateAdvertisement(), CancellationToken.None)).Value;

        await session.DisposeAsync();

        Assert.Equal(PeerAdvertisementState.Stopped, session.State);
    }
}
