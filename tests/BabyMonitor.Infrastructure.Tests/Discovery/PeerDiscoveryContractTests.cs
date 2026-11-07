using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Discovery;

/// <summary>
/// Общий набор contract tests для любой реализации IPeerDiscovery. Наследник задаёт фабрику
/// успешной и неуспешной реализации; тесты не зависят от Android/сети.
/// </summary>
public abstract class PeerDiscoveryContractTests
{
    protected abstract IPeerDiscovery CreateDiscovery();

    protected abstract IPeerDiscovery CreateFailingDiscovery();

    protected static DiscoveredPeer CreatePeer() =>
        new("peer-1", new TransportEndpoint("192.0.2.10", 50000), DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion);

    [Fact]
    public async Task StartAsync_возвращает_сессию_в_состоянии_Discovering()
    {
        var discovery = CreateDiscovery();

        var result = await discovery.StartAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, "Ожидался успешный Result запуска discovery.");
        await using var session = result.Value;
        Assert.Equal(PeerDiscoveryState.Discovering, session.State);
    }

    [Fact]
    public async Task StartAsync_при_отказе_возвращает_типизированную_ошибку()
    {
        var discovery = CreateFailingDiscovery();

        var result = await discovery.StartAsync(CancellationToken.None);

        Assert.False(result.IsSuccess, "Отказ discovery должен возвращаться как Result.");
        Assert.NotNull(result.Error);
        Assert.False(string.IsNullOrWhiteSpace(result.Error.Code));
    }

    [Fact]
    public async Task StartAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        var discovery = CreateDiscovery();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await discovery.StartAsync(cts.Token));
    }

    [Fact]
    public async Task StopAsync_переводит_сессию_в_Stopped_и_идемпотентен()
    {
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;

        await session.StopAsync(CancellationToken.None);
        await session.StopAsync(CancellationToken.None);

        Assert.Equal(PeerDiscoveryState.Stopped, session.State);
    }

    [Fact]
    public async Task DiscoverPeersAsync_завершается_после_остановки_сессии()
    {
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in session.DiscoverPeersAsync(cts.Token))
            {
                // Peer-ы, полученные до Stop, допустимы; важно, что поток завершается.
            }
        });

        await session.StopAsync(CancellationToken.None);
        await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task StopAsync_с_отменённым_токеном_переводит_сессию_в_Stopped()
    {
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await session.StopAsync(cts.Token);

        Assert.Equal(PeerDiscoveryState.Stopped, session.State);
    }

    [Fact]
    public async Task DiscoverPeersAsync_с_отменой_перечисления_бросает_OperationCanceledException()
    {
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;
        using var cancellation = new CancellationTokenSource();
        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in session.DiscoverPeersAsync(cancellation.Token))
            {
            }
        });

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task StateChanged_уведомляет_о_Stopped_ровно_один_раз()
    {
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;
        var stoppedEvents = 0;
        session.StateChanged += (_, args) =>
        {
            if (args.State is PeerDiscoveryState.Stopped)
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
        var discovery = CreateDiscovery();
        await using var session = (await discovery.StartAsync(CancellationToken.None)).Value;

        await session.DisposeAsync();

        Assert.Equal(PeerDiscoveryState.Stopped, session.State);
    }
}
