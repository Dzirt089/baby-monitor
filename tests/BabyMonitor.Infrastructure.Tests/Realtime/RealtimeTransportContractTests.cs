using BabyMonitor.Application.Abstractions.Realtime;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Общий набор contract tests для любой реализации <see cref="IRealtimeTransport"/>.
/// Наследник задаёт фабрики успешной и неуспешной реализации; тесты не зависят от библиотеки.
/// </summary>
public abstract class RealtimeTransportContractTests
{
    /// <summary>Успешно соединяющаяся реализация транспорта.</summary>
    protected abstract IRealtimeTransport CreateTransport();

    /// <summary>Реализация, у которой попытка соединения ожидаемо завершается ошибкой.</summary>
    protected abstract IRealtimeTransport CreateFailingTransport();

    /// <summary>Тестовый endpoint; реализация не обязана быть реально доступной для fake.</summary>
    // TEST-NET-1 (RFC 5737): пример адреса, не реальный private IP.
    protected static TransportEndpoint Endpoint => new("192.0.2.10", 50000);

    /// <summary>
    /// Окно наблюдения за heartbeat-событием после закрытия: реализации не обязаны присылать
    /// heartbeat немедленно, поэтому проверяется именно отсутствие событий после терминального состояния.
    /// </summary>
    protected virtual TimeSpan HeartbeatObservationWindow => TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task ConnectAsync_возвращает_успешную_сессию_в_состоянии_Connected()
    {
        var transport = CreateTransport();

        var result = await transport.ConnectAsync(Endpoint, CancellationToken.None);

        Assert.True(result.IsSuccess, "Ожидался успешный Result соединения.");
        var session = result.Value;
        Assert.Equal(RealtimeTransportState.Connected, session.State);
        await session.CloseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ConnectAsync_недоступный_peer_возвращает_ошибку_без_исключения()
    {
        var transport = CreateFailingTransport();

        var result = await transport.ConnectAsync(Endpoint, CancellationToken.None);

        Assert.False(result.IsSuccess, "Недоступный peer должен возвращать типизированную ошибку.");
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task ConnectAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        var transport = CreateTransport();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await transport.ConnectAsync(Endpoint, cts.Token));
    }

    [Fact]
    public async Task CloseAsync_переводит_сессию_в_Closed_и_идемпотентен()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;

        await session.CloseAsync(CancellationToken.None);
        await session.CloseAsync(CancellationToken.None);

        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task SendAudioAsync_после_закрытия_бросает_RealtimeTransportClosedException()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        await session.CloseAsync(CancellationToken.None);

        await Assert.ThrowsAsync<RealtimeTransportClosedException>(
            async () => await session.SendAudioAsync(new byte[] { 1, 2, 3 }, CancellationToken.None));
    }

    [Fact]
    public async Task SendAudioAsync_с_отменённым_токеном_бросает_OperationCanceledException()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await session.SendAudioAsync(new byte[] { 1, 2, 3 }, cts.Token));

        await session.CloseAsync(CancellationToken.None);
    }

    [Fact]
    public async Task SendAudioAsync_с_пустым_кадром_бросает_ArgumentException()
    {
        var transport = CreateTransport();
        await using var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;

        await Assert.ThrowsAsync<ArgumentException>(
            async () => await session.SendAudioAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task CloseAsync_с_отменённым_токеном_всё_равно_закрывает_сессию()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await session.CloseAsync(cts.Token);

        Assert.Equal(RealtimeTransportState.Closed, session.State);
        Assert.Null(session.LastError);
    }

    [Fact]
    public async Task StateChanged_уведомляет_о_закрытии_ровно_один_раз()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        var closedEvents = 0;
        session.StateChanged += (_, args) =>
        {
            if (args.State is RealtimeTransportState.Closed)
            {
                Assert.Equal(RealtimeTransportState.Closed, session.State);
                Assert.Null(args.Error);
                closedEvents++;
            }
        };

        await session.CloseAsync(CancellationToken.None);
        await session.CloseAsync(CancellationToken.None);

        Assert.Equal(1, closedEvents);
    }

    [Fact]
    public async Task ReceiveAudioAsync_завершается_после_закрытия_сессии()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = 0;

        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in session.ReceiveAudioAsync(cts.Token))
            {
                received++;
            }
        });

        await session.CloseAsync(CancellationToken.None);
        await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task DisposeAsync_закрывает_сессию()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;

        await session.DisposeAsync();

        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task DisposeAsync_идемпотентен_при_повторном_вызове()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;

        await session.DisposeAsync();
        await session.DisposeAsync();

        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task ReceiveAudioAsync_после_закрытия_завершается_без_кадра_и_исключения()
    {
        var transport = CreateTransport();
        await using var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        await session.CloseAsync(CancellationToken.None);
        var received = 0;

        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in session.ReceiveAudioAsync(CancellationToken.None))
            {
                received++;
            }
        });

        // Ограниченный таймаут: зависшее перечисление после Closed считается нарушением контракта.
        await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, received);
    }

    [Fact]
    public async Task ReceiveAudioAsync_с_отменой_перечисления_бросает_OperationCanceledException()
    {
        var transport = CreateTransport();
        await using var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        using var cancellation = new CancellationTokenSource();
        var enumerateTask = Task.Run(async () =>
        {
            await foreach (var _ in session.ReceiveAudioAsync(cancellation.Token))
            {
            }
        });

        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await enumerateTask.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task HeartbeatReceived_после_закрытия_не_поднимается()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        await session.CloseAsync(CancellationToken.None);
        var heartbeatsAfterClose = 0;
        session.HeartbeatReceived += (_, _) => Interlocked.Increment(ref heartbeatsAfterClose);

        await Task.Delay(HeartbeatObservationWindow);

        Assert.Equal(0, heartbeatsAfterClose);
        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task HeartbeatReceived_подписка_и_отписка_безопасны()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        EventHandler handler = (_, _) => { };
        session.HeartbeatReceived += handler;
        session.HeartbeatReceived -= handler;

        await session.CloseAsync(CancellationToken.None);

        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task StateChanged_не_уведомляет_после_закрытия_сессии()
    {
        var transport = CreateTransport();
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        await session.CloseAsync(CancellationToken.None);
        var statesAfterClose = new List<RealtimeTransportState>();
        session.StateChanged += (_, args) => statesAfterClose.Add(args.State);

        await session.CloseAsync(CancellationToken.None);
        await session.DisposeAsync();

        Assert.Empty(statesAfterClose);
        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }
}
