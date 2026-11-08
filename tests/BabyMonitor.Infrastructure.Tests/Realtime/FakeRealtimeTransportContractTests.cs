using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Проверка самого набора contract tests на ручном test double. Реальные адаптеры
/// (SIPSorcery/libwebrtc) добавляют наследника с собственными фабриками в spike 001.
/// </summary>
public sealed class FakeRealtimeTransportContractTests : RealtimeTransportContractTests
{
    protected override IRealtimeTransport CreateTransport() => new FakeRealtimeTransport();

    protected override IRealtimeTransport CreateFailingTransport() => new FakeRealtimeTransport(failConnect: true);

    [Fact]
    public async Task SendAudioAsync_передаёт_кадры_в_сессию()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        byte[] frame = [10, 20, 30];

        await session.SendAudioAsync(frame, CancellationToken.None);

        Assert.Single(session.SentFrames);
        Assert.Equal(frame, session.SentFrames[0]);
    }

    [Fact]
    public async Task ReceiveAudioAsync_возвращает_подготовленные_входящие_кадры()
    {
        var transport = new FakeRealtimeTransport(incomingFrames: [new byte[] { 7, 8 }, new byte[] { 9 }]);
        var session = (await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = new List<byte[]>();

        // Перечисление завершится только после закрытия сессии, поэтому читаем ожидаемые кадры
        // и выходим из цикла, не дожидаясь завершения потока.
        await using (var enumerator = session.ReceiveAudioAsync(cts.Token).GetAsyncEnumerator(cts.Token))
        {
            while (received.Count < 2 && await enumerator.MoveNextAsync())
            {
                received.Add(enumerator.Current.Payload.ToArray());
            }
        }

        await session.CloseAsync(CancellationToken.None);

        Assert.Equal(2, received.Count);
        Assert.Equal(new byte[] { 7, 8 }, received[0]);
        Assert.Equal(new byte[] { 9 }, received[1]);
    }

    [Fact]
    public async Task SendAudioAsync_с_пустым_кадром_бросает_ArgumentException_и_ничего_не_отправляет()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;

        // Порт документирует ArgumentException для пустого кадра; проверяем и то, что кадр не попал в очередь.
        var exception = await Assert.ThrowsAsync<ArgumentException>(
            async () => await session.SendAudioAsync(ReadOnlyMemory<byte>.Empty, CancellationToken.None));

        Assert.Equal("encodedAudioFrame", exception.ParamName);
        Assert.Empty(session.SentFrames);
    }

    [Fact]
    public async Task Fail_переводит_сессию_в_Failed_и_сохраняет_LastError()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        var failure = new Error("transport.media_timeout", "Media flow не подтверждён.");
        RealtimeTransportStateChangedEventArgs? failedEvent = null;
        session.StateChanged += (_, args) =>
        {
            if (args.State is RealtimeTransportState.Failed)
            {
                failedEvent = args;
            }
        };

        session.Fail(failure);

        Assert.Equal(RealtimeTransportState.Failed, session.State);
        Assert.Same(failure, session.LastError);
        Assert.NotNull(failedEvent);
        Assert.Same(failure, failedEvent.Error);
    }

    [Fact]
    public async Task SendAudioAsync_после_Failed_бросает_RealtimeTransportClosedException()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        session.Fail(new Error("transport.failed", "Транспорт завершился ошибкой."));

        await Assert.ThrowsAsync<RealtimeTransportClosedException>(
            async () => await session.SendAudioAsync(new byte[] { 1 }, CancellationToken.None));
    }

    [Fact]
    public async Task CloseAsync_после_Failed_переводит_сессию_в_Closed()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        session.Fail(new Error("transport.failed", "Транспорт завершился ошибкой."));

        await session.CloseAsync(CancellationToken.None);

        Assert.Equal(RealtimeTransportState.Closed, session.State);
    }

    [Fact]
    public async Task SimulateHeartbeat_поднимает_событие_только_в_Connected()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        var heartbeats = 0;
        session.HeartbeatReceived += (_, _) => heartbeats++;

        session.SimulateHeartbeat();
        Assert.Equal(1, heartbeats);

        await session.CloseAsync(CancellationToken.None);
        session.SimulateHeartbeat();
        Assert.Equal(1, heartbeats);
    }

    [Fact]
    public async Task StateChanged_сообщает_сначала_Failed_затем_Closed()
    {
        var transport = new FakeRealtimeTransport();
        var session = (FakeRealtimeTransportSession)(await transport.ConnectAsync(Endpoint, CancellationToken.None)).Value;
        var states = new List<RealtimeTransportState>();
        session.StateChanged += (_, args) => states.Add(args.State);

        session.Fail(new Error("transport.failed", "Транспорт завершился ошибкой."));
        await session.CloseAsync(CancellationToken.None);

        Assert.Equal([RealtimeTransportState.Failed, RealtimeTransportState.Closed], states);
    }
}
