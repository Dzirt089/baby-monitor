using System.Runtime.CompilerServices;
using System.Threading.Channels;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Abstractions.Connectivity;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Media;

/// <summary>
/// Управляемое время с ручными таймерами: Task.Delay(delay, timeProvider, ct) и таймеры
/// координатора срабатывают только при <see cref="Advance"/>, поэтому reconnect-тесты детерминированы
/// и не спят реальное время.
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_sync)
        {
            return _now;
        }
    }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        ArgumentNullException.ThrowIfNull(callback);

        var timer = new ManualTimer(this, callback, state, dueTime, period);
        lock (_sync)
        {
            timer.ScheduleLocked(_now);
            _timers.Add(timer);
        }

        return timer;
    }

    /// <summary>Продвигает управляемое время и выполняет все наступившие таймеры.</summary>
    public void Advance(TimeSpan delta)
    {
        DateTimeOffset now;
        lock (_sync)
        {
            _now += delta;
            now = _now;
        }

        while (true)
        {
            ManualTimer? due;
            lock (_sync)
            {
                due = _timers.FirstOrDefault(timer => timer.IsDueLocked(now));
            }

            if (due is null)
            {
                return;
            }

            due.Fire(now);
        }
    }

    /// <summary>Количество запланированных, ещё не сработавших таймеров.</summary>
    public int PendingTimerCount
    {
        get
        {
            lock (_sync)
            {
                return _timers.Count;
            }
        }
    }

    internal object Sync => _sync;

    internal DateTimeOffset NowLocked => _now;

    internal void RemoveLocked(ManualTimer timer) => _timers.Remove(timer);

    internal sealed class ManualTimer : ITimer
    {
        private readonly ManualTimeProvider _owner;
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private readonly TimeSpan _dueTime;
        private TimeSpan _period;
        private DateTimeOffset? _dueAt;
        private bool _disposed;

        public ManualTimer(
            ManualTimeProvider owner,
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            _owner = owner;
            _callback = callback;
            _state = state;
            _dueTime = dueTime;
            _period = period;
        }

        internal void ScheduleLocked(DateTimeOffset now) =>
            _dueAt = _dueTime == Timeout.InfiniteTimeSpan
                ? null
                : now + (_dueTime < TimeSpan.Zero ? TimeSpan.Zero : _dueTime);

        internal bool IsDueLocked(DateTimeOffset now) =>
            !_disposed && _dueAt is { } due && due <= now;

        internal void Fire(DateTimeOffset now)
        {
            TimerCallback callback;
            object? state;
            lock (_owner.Sync)
            {
                if (_disposed)
                {
                    return;
                }

                if (_period > TimeSpan.Zero)
                {
                    _dueAt = now + _period;
                }
                else
                {
                    _disposed = true;
                    _owner.RemoveLocked(this);
                }

                callback = _callback;
                state = _state;
            }

            callback(state);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (_owner.Sync)
            {
                if (_disposed)
                {
                    return false;
                }

                _period = period;
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : _owner.NowLocked + dueTime;
                return true;
            }
        }

        public void Dispose()
        {
            lock (_owner.Sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _owner.RemoveLocked(this);
            }
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>Fake IPeerDiscovery: отдаёт новые сессии и позволяет добавить обнаруженный peer.</summary>
internal sealed class FakePeerDiscovery : IPeerDiscovery
{
    private readonly object _sync = new();
    private TaskCompletionSource<int> _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int StartCalls { get; private set; }

    public Error? StartError { get; set; }

    /// <summary>Peer, автоматически добавляемый в каждую новую discovery-сессию (retry-тесты).</summary>
    public DiscoveredPeer? AutoPeer { get; set; }

    public List<FakePeerDiscoverySession> Sessions { get; } = new();

    public FakePeerDiscoverySession? LastSession { get; private set; }

    public Task<Result<IPeerDiscoverySession>> StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<int> signal;
        FakePeerDiscoverySession? session = null;
        Error? error;
        lock (_sync)
        {
            StartCalls++;
            error = StartError;
            if (error is null)
            {
                session = new FakePeerDiscoverySession();
                if (AutoPeer is { } autoPeer)
                {
                    session.AddPeer(autoPeer);
                }

                LastSession = session;
                Sessions.Add(session);
            }

            signal = _startSignal;
            _startSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult(StartCalls);

        return error is null
            ? Task.FromResult(Result<IPeerDiscoverySession>.Success(session!))
            : Task.FromResult(Result<IPeerDiscoverySession>.Failure(error));
    }

    public async Task WaitForStartAsync(int callNumber)
    {
        while (true)
        {
            Task signal;
            lock (_sync)
            {
                if (StartCalls >= callNumber)
                {
                    return;
                }

                signal = _startSignal.Task;
            }

            await signal.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }
}

/// <summary>Одна discovery-сессия: канал обнаруженных peer-ов и учёт Stop/Dispose.</summary>
internal sealed class FakePeerDiscoverySession : IPeerDiscoverySession
{
    private readonly Channel<DiscoveredPeer> _peers = Channel.CreateUnbounded<DiscoveredPeer>();

    public PeerDiscoveryState State { get; private set; } = PeerDiscoveryState.Discovering;

    public Error? LastError { get; private set; }

    public event EventHandler<PeerDiscoveryStateChangedEventArgs>? StateChanged;

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    /// <summary>Исключение, которое enumerator бросает на следующем MoveNext после выдачи peer-а.</summary>
    public Exception? EnumerationException { get; set; }

    public void AddPeer(DiscoveredPeer peer) => _peers.Writer.TryWrite(peer);

    public async IAsyncEnumerable<DiscoveredPeer> DiscoverPeersAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var peer in _peers.Reader.ReadAllAsync(cancellationToken))
        {
            yield return peer;
            if (EnumerationException is { } failure)
            {
                throw failure;
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCalls++;
        State = PeerDiscoveryState.Stopped;
        _peers.Writer.TryComplete();
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Stopped));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        _peers.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public void Fail(Error error)
    {
        LastError = error;
        State = PeerDiscoveryState.Failed;
        StateChanged?.Invoke(this, new PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState.Failed, error));
    }
}

/// <summary>Fake IPeerAdvertiser: фиксирует advertisement и освобождение сессии.</summary>
internal sealed class FakePeerAdvertiser : IPeerAdvertiser
{
    public int StartCalls { get; private set; }

    public PeerAdvertisement? LastAdvertisement { get; private set; }

    public Error? StartError { get; set; }

    private readonly Queue<Error> _startFailures = new();

    public FakePeerAdvertiserSession? LastSession { get; private set; }

    /// <summary>Ставит ошибку для следующего StartAsync (после неё StartError/успех снова действуют).</summary>
    public void EnqueueStartFailure(Error error) => _startFailures.Enqueue(error);

    public Task<Result<IPeerAdvertiserSession>> StartAsync(
        PeerAdvertisement advertisement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(advertisement);
        StartCalls++;
        LastAdvertisement = advertisement;
        Error? failure = _startFailures.Count > 0 ? _startFailures.Dequeue() : StartError;
        if (failure is not null)
        {
            return Task.FromResult(Result<IPeerAdvertiserSession>.Failure(failure));
        }

        LastSession = new FakePeerAdvertiserSession();
        return Task.FromResult(Result<IPeerAdvertiserSession>.Success(LastSession));
    }
}

/// <summary>Одна advertise-сессия: учёт Stop/Dispose и состояния.</summary>
internal sealed class FakePeerAdvertiserSession : IPeerAdvertiserSession
{
    public PeerAdvertisementState State { get; private set; } = PeerAdvertisementState.Advertising;

    public Error? LastError => null;

    public event EventHandler<PeerAdvertisementStateChangedEventArgs>? StateChanged;

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCalls++;
        State = PeerAdvertisementState.Stopped;
        StateChanged?.Invoke(this, new PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState.Stopped));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Fake IRealtimeTransport: записывает endpoint и отдаёт настроенные сессии.</summary>
internal sealed class FakeRealtimeTransport : IRealtimeTransport
{
    public int ConnectCalls { get; private set; }

    public List<TransportEndpoint> Endpoints { get; } = new();

    public Func<CancellationToken, Task<Result<IRealtimeTransportSession>>>? ConnectHandler { get; set; }

    public Task<Result<IRealtimeTransportSession>> ConnectAsync(
        TransportEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        ConnectCalls++;
        Endpoints.Add(endpoint);
        return ConnectHandler is { } handler
            ? handler(cancellationToken)
            : Task.FromResult(Result<IRealtimeTransportSession>.Success(new FakeRealtimeTransportSession()));
    }
}

/// <summary>
/// Fake transport-сессия: отправленные Opus-кадры, канал принятых кадров и управляемый переход
/// в Failed/Closed (StateChanged + LastError).
/// </summary>
internal sealed class FakeRealtimeTransportSession : IRealtimeTransportSession
{
    private readonly object _sync = new();
    private readonly List<byte[]> _sentFrames = new();
    private readonly Channel<ReceivedAudioFrame> _received = Channel.CreateUnbounded<ReceivedAudioFrame>();

    public RealtimeTransportState State { get; private set; } = RealtimeTransportState.Connected;

    public Error? LastError { get; private set; }

    public event EventHandler<RealtimeTransportStateChangedEventArgs>? StateChanged;

    public event EventHandler? HeartbeatReceived;

    public int SendAudioCalls { get; private set; }

    public int CloseCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public int HeartbeatCount { get; private set; }

    public IReadOnlyList<byte[]> GetSentFrames()
    {
        lock (_sync)
        {
            return _sentFrames.Select(frame => frame.ToArray()).ToArray();
        }
    }

    public ValueTask SendAudioAsync(ReadOnlyMemory<byte> encodedAudioFrame, CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            if (State is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                throw new RealtimeTransportClosedException();
            }

            SendAudioCalls++;
            _sentFrames.Add(encodedAudioFrame.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<ReceivedAudioFrame> ReceiveAudioAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in _received.Reader.ReadAllAsync(cancellationToken))
        {
            yield return frame;
        }
    }

    public Task CloseAsync(CancellationToken cancellationToken)
    {
        CloseCalls++;
        State = RealtimeTransportState.Closed;
        _received.Writer.TryComplete();
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Closed));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        _received.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    public void PushEncodedFrame(byte[] encoded)
    {
        if (encoded.Length == 0)
        {
            throw new ArgumentException("Opus-кадр не может быть пустым.", nameof(encoded));
        }

        _received.Writer.TryWrite(new ReceivedAudioFrame(encoded));
    }

    public void Fail(Error error)
    {
        State = RealtimeTransportState.Failed;
        LastError = error;
        _received.Writer.TryComplete();
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Failed, error));
    }

    public void FailSilently(Error error)
    {
        State = RealtimeTransportState.Failed;
        LastError = error;
        _received.Writer.TryComplete();
    }

    /// <summary>Имитирует полученный control heartbeat (pong) от peer.</summary>
    public void RaiseHeartbeat()
    {
        HeartbeatCount++;
        HeartbeatReceived?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>Fake IRealtimeTransportListener: порт, очередь ошибок старта, фабрика принятых сессий.</summary>
internal sealed class FakeRealtimeTransportListener : IRealtimeTransportListener
{
    private readonly object _sync = new();
    private readonly Queue<Error> _startFailures = new();
    private readonly Queue<Error> _acceptFailures = new();
    private TaskCompletionSource<int> _startSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource<int> _acceptSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int BoundPort { get; set; } = PeerDiscoveryConstants.ProvisionalTransportPort;

    public int StartCalls { get; private set; }

    public List<int> RequestedPorts { get; } = new();

    public List<FakeRealtimeTransportListenerSession> Sessions { get; } = new();

    public Func<FakeRealtimeTransportSession>? TransportFactory { get; set; }

    public int AcceptCalls { get; private set; }

    public FakeRealtimeTransportSession? LastAcceptedTransport { get; private set; }

    public void EnqueueStartFailure(Error error)
    {
        lock (_sync)
        {
            _startFailures.Enqueue(error);
        }
    }

    /// <summary>Ставит peer-level отказ для следующего AcceptAsync (listener остаётся пригодным).</summary>
    public void EnqueueAcceptFailure(Error error)
    {
        lock (_sync)
        {
            _acceptFailures.Enqueue(error);
        }
    }

    public Task<Result<IRealtimeTransportListenerSession>> StartAsync(int port, CancellationToken cancellationToken)
    {
        TaskCompletionSource<int> signal;
        Error? failure = null;
        FakeRealtimeTransportListenerSession? session = null;
        lock (_sync)
        {
            StartCalls++;
            RequestedPorts.Add(port);
            if (_startFailures.Count > 0)
            {
                failure = _startFailures.Dequeue();
            }
            else
            {
                session = new FakeRealtimeTransportListenerSession(this, BoundPort);
                Sessions.Add(session);
            }

            signal = _startSignal;
            _startSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        signal.TrySetResult(StartCalls);

        return failure is null
            ? Task.FromResult(Result<IRealtimeTransportListenerSession>.Success(session!))
            : Task.FromResult(Result<IRealtimeTransportListenerSession>.Failure(failure));
    }

    public async Task WaitForStartAsync(int callNumber)
    {
        while (true)
        {
            Task signal;
            lock (_sync)
            {
                if (StartCalls >= callNumber)
                {
                    return;
                }

                signal = _startSignal.Task;
            }

            await signal.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    public async Task<FakeRealtimeTransportSession> WaitForAcceptAsync(int callNumber)
    {
        while (true)
        {
            Task signal;
            lock (_sync)
            {
                if (AcceptCalls >= callNumber && LastAcceptedTransport is { } transport)
                {
                    return transport;
                }

                signal = _acceptSignal.Task;
            }

            await signal.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    internal Result<IRealtimeTransportSession> AcceptNextTransport()
    {
        lock (_sync)
        {
            AcceptCalls++;
            if (_acceptFailures.Count > 0)
            {
                return Result<IRealtimeTransportSession>.Failure(_acceptFailures.Dequeue());
            }

            var transport = TransportFactory?.Invoke() ?? new FakeRealtimeTransportSession();
            LastAcceptedTransport = transport;
            var signal = _acceptSignal;
            _acceptSignal = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            signal.TrySetResult(AcceptCalls);
            return Result<IRealtimeTransportSession>.Success(transport);
        }
    }
}

/// <summary>Одна listener-сессия: принимает peer по фабрике теста.</summary>
internal sealed class FakeRealtimeTransportListenerSession : IRealtimeTransportListenerSession
{
    private readonly FakeRealtimeTransportListener _listener;

    public FakeRealtimeTransportListenerSession(FakeRealtimeTransportListener listener, int boundPort)
    {
        _listener = listener;
        BoundPort = boundPort;
    }

    public int BoundPort { get; }

    public int DisposeCalls { get; private set; }

    public Task<Result<IRealtimeTransportSession>> AcceptAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_listener.AcceptNextTransport());

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Fake IAudioCapture: старт, параметры, сессия с каналом PCM-кадров.</summary>
internal sealed class FakeAudioCapture : IAudioCapture
{
    public int StartCalls { get; private set; }

    public AudioCaptureOptions? LastOptions { get; private set; }

    public Error? StartError { get; set; }

    public List<FakeAudioCaptureSession> Sessions { get; } = new();

    public FakeAudioCaptureSession? LastSession { get; private set; }

    /// <summary>Исключение платформенного адаптера, возникающее при каждом ReadAsync.</summary>
    public Exception? ReadException { get; set; }

    public Task<Result<IAudioCaptureSession>> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        StartCalls++;
        LastOptions = options;
        if (StartError is { } error)
        {
            return Task.FromResult(Result<IAudioCaptureSession>.Failure(error));
        }

        LastSession = new FakeAudioCaptureSession(options.Format) { ReadException = ReadException };
        Sessions.Add(LastSession);
        return Task.FromResult(Result<IAudioCaptureSession>.Success(LastSession));
    }
}

/// <summary>Одна capture-сессия: PCM-кадры из канала и учёт Stop/Dispose.</summary>
internal sealed class FakeAudioCaptureSession : IAudioCaptureSession
{
    private readonly Channel<byte[]> _frames = Channel.CreateUnbounded<byte[]>();

    public FakeAudioCaptureSession(AudioFormat format) => Format = format;

    public AudioFormat Format { get; }

    /// <summary>Исключение платформенного адаптера (IOException/AndroidAudioAdapterException).</summary>
    public Exception? ReadException { get; set; }

    public int ReadCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public void PushPcm(params byte[][] frames)
    {
        foreach (var frame in frames)
        {
            _frames.Writer.TryWrite(frame);
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        ReadCalls++;
        if (destination.Length < Format.Frame20MsBytes)
        {
            throw new ArgumentException("Буфер меньше одного кадра 20 мс.", nameof(destination));
        }

        if (ReadException is { } failure)
        {
            throw failure;
        }

        while (await _frames.Reader.WaitToReadAsync(cancellationToken))
        {
            if (_frames.Reader.TryRead(out var frame))
            {
                var length = Math.Min(frame.Length, destination.Length);
                frame.AsSpan(0, length).CopyTo(destination.Span);
                return length;
            }
        }

        return 0;
    }

    /// <summary>Завершает поток PCM без StopAsync: следующее ReadAsync вернёт 0 (конец потока).</summary>
    public void CompleteStream() => _frames.Writer.TryComplete();

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCalls++;
        _frames.Writer.TryComplete();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        _frames.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Fake IAudioPlayback: старт, параметры, сессия с записанными PCM-блоками.</summary>
internal sealed class FakeAudioPlayback : IAudioPlayback
{
    public int StartCalls { get; private set; }

    public AudioPlaybackOptions? LastOptions { get; private set; }

    public Error? StartError { get; set; }

    public List<FakeAudioPlaybackSession> Sessions { get; } = new();

    public FakeAudioPlaybackSession? LastSession { get; private set; }

    /// <summary>Исключение платформенного адаптера, возникающее при каждом WriteAsync.</summary>
    public Exception? WriteException { get; set; }

    public Task<Result<IAudioPlaybackSession>> StartAsync(
        AudioPlaybackOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        StartCalls++;
        LastOptions = options;
        if (StartError is { } error)
        {
            return Task.FromResult(Result<IAudioPlaybackSession>.Failure(error));
        }

        LastSession = new FakeAudioPlaybackSession(options.Format) { WriteException = WriteException };
        Sessions.Add(LastSession);
        return Task.FromResult(Result<IAudioPlaybackSession>.Success(LastSession));
    }
}

/// <summary>Одна playback-сессия: записанные PCM 20 мс блоки и учёт Stop/Dispose.</summary>
internal sealed class FakeAudioPlaybackSession : IAudioPlaybackSession
{
    private readonly object _sync = new();
    private readonly List<byte[]> _written = new();

    public FakeAudioPlaybackSession(AudioFormat format) => Format = format;

    public AudioFormat Format { get; }

    /// <summary>Исключение платформенного адаптера (IOException/AndroidAudioAdapterException).</summary>
    public Exception? WriteException { get; set; }

    public int WriteCalls { get; private set; }

    public int StopCalls { get; private set; }

    public int DisposeCalls { get; private set; }

    public IReadOnlyList<byte[]> GetWrittenPcm()
    {
        lock (_sync)
        {
            return _written.Select(block => block.ToArray()).ToArray();
        }
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        if (WriteException is { } failure)
        {
            throw failure;
        }

        lock (_sync)
        {
            WriteCalls++;
            _written.Add(pcm.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        StopCalls++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        return ValueTask.CompletedTask;
    }
}

/// <summary>Fake IAudioCodec: фиксирует длины PCM/Opus кадров, encode → 2 байта, decode → 20 мс PCM.</summary>
internal sealed class FakeAudioCodec : IAudioCodec, IDisposable
{
    private readonly object _sync = new();
    private readonly List<int> _encodedPcmLengths = new();
    private readonly List<int> _decodedFrameLengths = new();

    public AudioFormat Format => AudioFormat.Default;

    public int MaxEncodedBytes => 1275;

    public int DisposeCalls { get; private set; }

    public byte[] EncodedPayload { get; set; } = [0xAA, 0xBB];

    public byte[] DecodedPayloadFill => [0x55];

    public IReadOnlyList<int> GetEncodedPcmLengths()
    {
        lock (_sync)
        {
            return _encodedPcmLengths.ToArray();
        }
    }

    public IReadOnlyList<int> GetDecodedFrameLengths()
    {
        lock (_sync)
        {
            return _decodedFrameLengths.ToArray();
        }
    }

    public int Encode(ReadOnlySpan<byte> pcm, Span<byte> encodedDestination)
    {
        lock (_sync)
        {
            _encodedPcmLengths.Add(pcm.Length);
        }

        if (pcm.Length != Format.Frame20MsBytes)
        {
            throw new ArgumentException("Ожидался ровно один PCM-кадр 20 мс.", nameof(pcm));
        }

        EncodedPayload.AsSpan().CopyTo(encodedDestination);
        return EncodedPayload.Length;
    }

    public void Dispose() => DisposeCalls++;

    public int Decode(ReadOnlySpan<byte> encoded, Span<byte> pcmDestination)
    {
        lock (_sync)
        {
            _decodedFrameLengths.Add(encoded.Length);
        }

        var length = Math.Min(Format.Frame20MsBytes, pcmDestination.Length);
        pcmDestination[..length].Fill(DecodedPayloadFill[0]);
        return length;
    }
}

/// <summary>Fake IConnectivityMonitor: тест поднимает NetworkChanged вручную.</summary>
internal sealed class FakeConnectivityMonitor : IConnectivityMonitor
{
    private readonly object _sync = new();
    private readonly Queue<Result<NetworkSnapshot>> _snapshotSequence = new();

    public event EventHandler? NetworkChanged;

    public int DisposeCalls { get; private set; }

    public Result<NetworkSnapshot> SnapshotResult { get; set; } = Result<NetworkSnapshot>.Success(new NetworkSnapshot(true));

    /// <summary>Ставит снимок для следующего GetSnapshotAsync (приоритетнее SnapshotResult).</summary>
    public void EnqueueSnapshot(Result<NetworkSnapshot> snapshot)
    {
        lock (_sync)
        {
            _snapshotSequence.Enqueue(snapshot);
        }
    }

    public void GoOffline() => SnapshotResult = Result<NetworkSnapshot>.Success(new NetworkSnapshot(false));

    public void GoOnline() => SnapshotResult = Result<NetworkSnapshot>.Success(new NetworkSnapshot(true));

    public Task<Result<NetworkSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            return Task.FromResult(
                _snapshotSequence.Count > 0 ? _snapshotSequence.Dequeue() : SnapshotResult);
        }
    }

    public ValueTask DisposeAsync()
    {
        DisposeCalls++;
        NetworkChanged = null;
        return ValueTask.CompletedTask;
    }

    public void RaiseNetworkChanged() => NetworkChanged?.Invoke(this, EventArgs.Empty);
}
