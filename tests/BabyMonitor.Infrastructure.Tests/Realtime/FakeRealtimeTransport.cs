using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Tests.Realtime;

/// <summary>
/// Ручной test double IRealtimeTransport для проверки контракта порта. Не production-адаптер:
/// хранит отправленные кадры в памяти и позволяет подставить входящие кадры для contract tests.
/// </summary>
internal sealed class FakeRealtimeTransport : IRealtimeTransport
{
    private readonly bool _failConnect;
    private readonly IReadOnlyList<byte[]> _incomingFrames;

    public FakeRealtimeTransport(bool failConnect = false, IReadOnlyList<byte[]>? incomingFrames = null)
    {
        _failConnect = failConnect;
        _incomingFrames = incomingFrames ?? [];
    }

    /// <inheritdoc />
    public Task<Result<IRealtimeTransportSession>> ConnectAsync(
        TransportEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<IRealtimeTransportSession>>(cancellationToken);
        }

        if (_failConnect)
        {
            return Task.FromResult(Result<IRealtimeTransportSession>.Failure(
                new Error("transport.connect_failed", "Не удалось установить transport-соединение.")));
        }

        var session = new FakeRealtimeTransportSession(_incomingFrames);
        session.MarkConnected();
        return Task.FromResult(Result<IRealtimeTransportSession>.Success(session));
    }
}

/// <summary>
/// Сессия test double: bounded входящая очередь, учёт отправленных кадров, события состояния.
/// Single-threaded: не потокобезопасна (списки/LastError); concurrency-контракт проверяется
/// при появлении реального адаптера (phase 2/3).
/// </summary>
internal sealed class FakeRealtimeTransportSession : IRealtimeTransportSession
{
    private const int IncomingQueueCapacity = 8;

    private readonly Channel<ReceivedAudioFrame> _incoming;
    private readonly List<byte[]> _sentFrames = [];
    private int _state = (int)RealtimeTransportState.Connecting;

    public FakeRealtimeTransportSession(IReadOnlyList<byte[]> incomingFrames)
    {
        _incoming = Channel.CreateBounded<ReceivedAudioFrame>(
            new BoundedChannelOptions(IncomingQueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

        foreach (var frame in incomingFrames)
        {
            _incoming.Writer.TryWrite(new ReceivedAudioFrame(frame));
        }
    }

    /// <inheritdoc />
    public RealtimeTransportState State => (RealtimeTransportState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public Error? LastError { get; private set; }

    /// <inheritdoc />
    public event EventHandler<RealtimeTransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public event EventHandler? HeartbeatReceived;

    /// <summary>Кадры, переданные через <see cref="SendAudioAsync"/> с момента создания сессии.</summary>
    public IReadOnlyList<byte[]> SentFrames => _sentFrames;

    /// <summary>Перевести сессию в Connected без прохождения реального handshake.</summary>
    public void MarkConnected()
    {
        Volatile.Write(ref _state, (int)RealtimeTransportState.Connected);
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Connected));
    }

    /// <inheritdoc />
    public ValueTask SendAudioAsync(ReadOnlyMemory<byte> encodedAudioFrame, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromCanceled(cancellationToken);
        }

        EnsureSessionUsable();

        if (encodedAudioFrame.IsEmpty)
        {
            throw new ArgumentException("Отправляемый аудиокадр не может быть пустым.", nameof(encodedAudioFrame));
        }

        _sentFrames.Add(encodedAudioFrame.ToArray());
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReceivedAudioFrame> ReceiveAudioAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in _incoming.Reader.ReadAllAsync(cancellationToken))
        {
            yield return frame;
        }
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        if (State is RealtimeTransportState.Closed)
        {
            return Task.CompletedTask;
        }

        // Отмена токена не отменяет закрытие: она ограничивает только ожидание graceful shutdown.
        Volatile.Write(ref _state, (int)RealtimeTransportState.Closed);
        _incoming.Writer.TryComplete();
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Closed));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None);
    }

    /// <summary>
    /// Симулировать подтверждённый pong: событие поднимается только в Connected (fake-specific
    /// проверка семантики контракта, реальный цикл — в SIPSorcery-адаптере).
    /// </summary>
    public void SimulateHeartbeat()
    {
        if (State is not RealtimeTransportState.Connected)
        {
            return;
        }

        HeartbeatReceived?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Перевести сессию в Failed с типизированной ошибкой (для проверки failure-контракта).</summary>
    /// <param name="error">Причина транспортного отказа.</param>
    public void Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        Volatile.Write(ref _state, (int)RealtimeTransportState.Failed);
        LastError = error;
        _incoming.Writer.TryComplete();
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Failed, error));
    }

    private void EnsureSessionUsable()
    {
        if (State is not RealtimeTransportState.Connected)
        {
            throw new RealtimeTransportClosedException($"Сессия не готова к отправке аудио: состояние {State}.");
        }
    }
}
