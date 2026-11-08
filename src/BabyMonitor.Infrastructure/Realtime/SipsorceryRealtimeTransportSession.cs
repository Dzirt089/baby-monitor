using System.Net;
using System.Threading.Channels;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;
using SIPSorcery.Net;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Активная WebRTC audio-only сессия SIPSorcery за <see cref="IRealtimeTransportSession"/>.
/// Signaling-канал (TLS + identity assertion) остаётся открытым для ping/pong/bye, media-поток —
/// DTLS-SRTP/Opus. Send/Receive используют bounded-очереди с drop-oldest: при перегрузке старые
/// кадры отбрасываются, latency не растёт безгранично, а потоки сети не блокируются. Аудио не
/// логируется и не сохраняется.
/// </summary>
public sealed class SipsorceryRealtimeTransportSession : IRealtimeTransportSession
{
    private const uint OpusFrameRtpUnits = SipsorceryMediaHandshake.OpusFrameRtpUnits;
    private static readonly TimeSpan GracefulCloseTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PumpShutdownTimeout = TimeSpan.FromSeconds(2);

    private readonly object _sync = new();
    private readonly TlsSignalingConnection _signalingConnection;
    private readonly RTCPeerConnection _peerConnection;
    private readonly Channel<byte[]> _sendQueue;
    private readonly Channel<ReceivedAudioFrame> _receiveQueue;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private readonly Task _sendPumpTask;
    private readonly Task _controlPumpTask;
    private int _state = (int)RealtimeTransportState.Connected;
    private int _closing;
    private Error? _lastError;
    private Task? _closeTask;

    internal SipsorceryRealtimeTransportSession(
        TlsSignalingConnection signalingConnection,
        RTCPeerConnection peerConnection,
        SipsorceryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(signalingConnection);
        ArgumentNullException.ThrowIfNull(peerConnection);
        ArgumentNullException.ThrowIfNull(options);

        _signalingConnection = signalingConnection;
        _peerConnection = peerConnection;
        _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(options.SendQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });
        _receiveQueue = Channel.CreateBounded<ReceivedAudioFrame>(new BoundedChannelOptions(options.ReceiveQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _peerConnection.OnRtpPacketReceived += OnRtpPacketReceived;
        _peerConnection.onconnectionstatechange += OnConnectionStateChanged;
        _sendPumpTask = SendPumpAsync();
        _controlPumpTask = ControlPumpAsync();
    }

    /// <inheritdoc />
    public RealtimeTransportState State => (RealtimeTransportState)Volatile.Read(ref _state);

    /// <inheritdoc />
    public Error? LastError
    {
        get
        {
            lock (_sync)
            {
                return _lastError;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<RealtimeTransportStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public ValueTask SendAudioAsync(ReadOnlyMemory<byte> encodedAudioFrame, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (encodedAudioFrame.IsEmpty)
        {
            throw new ArgumentException("Отправляемый аудиокадр не может быть пустым.", nameof(encodedAudioFrame));
        }

        if (State is not RealtimeTransportState.Connected)
        {
            throw new RealtimeTransportClosedException("Transport-сессия не готова к отправке аудио.");
        }

        // Копия кадра до возврата управления: вызывающий вправе переиспользовать буфер.
        _sendQueue.Writer.TryWrite(encodedAudioFrame.ToArray());
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ReceivedAudioFrame> ReceiveAudioAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var frame in _receiveQueue.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return frame;
        }
    }

    /// <inheritdoc />
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _closeTask ??= CloseCoreAsync(cancellationToken);
            return _closeTask;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await CloseAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task SendPumpAsync()
    {
        try
        {
            await foreach (var frame in _sendQueue.Reader.ReadAllAsync(_lifetimeCts.Token).ConfigureAwait(false))
            {
                try
                {
                    _peerConnection.SendAudio(OpusFrameRtpUnits, frame);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    // Граница адаптера: отказ media-стека преобразуется в типизированную ошибку сессии.
                    FailSession(SignalingErrors.MediaFlowLost());
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Lifetime отменён при локальном закрытии.
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
    }

    private async Task ControlPumpAsync()
    {
        try
        {
            while (!_lifetimeCts.IsCancellationRequested)
            {
                var message = await _signalingConnection.ReceiveAsync(_lifetimeCts.Token).ConfigureAwait(false);
                switch (message.Kind)
                {
                    case SignalingMessageKind.Ping:
                        await _signalingConnection
                            .SendPongAsync(message.ControlPayload, _lifetimeCts.Token)
                            .ConfigureAwait(false);
                        break;
                    case SignalingMessageKind.Pong:
                        break;
                    case SignalingMessageKind.Bye:
                        FailSession(SignalingErrors.MediaFlowLost());
                        return;
                    default:
                        FailSession(SignalingErrors.MediaFlowLost());
                        return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Lifetime отменён при локальном закрытии.
        }
        catch (SignalingConnectionClosedException)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
        catch (SignalingProtocolException)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
        catch (IOException)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
    }

    private void OnRtpPacketReceived(IPEndPoint remoteEndPoint, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket)
    {
        if (mediaType != SDPMediaTypesEnum.audio)
        {
            return;
        }

        var payload = rtpPacket.Payload;
        if (payload is null || payload.Length == 0)
        {
            return;
        }

        // Bounded drop-oldest: при перегрузке старые кадры теряются, transport-поток не блокируется.
        _receiveQueue.Writer.TryWrite(new ReceivedAudioFrame(payload.ToArray()));
    }

    private void OnConnectionStateChanged(RTCPeerConnectionState state)
    {
        if (Volatile.Read(ref _closing) == 1)
        {
            return;
        }

        // disconnected допускает восстановление ICE; failed/closed — терминальная потеря media-пути.
        if (state is RTCPeerConnectionState.failed or RTCPeerConnectionState.closed)
        {
            FailSession(SignalingErrors.MediaFlowLost());
        }
    }

    private void FailSession(Error error)
    {
        lock (_sync)
        {
            var current = State;
            if (current is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                return;
            }

            Volatile.Write(ref _state, (int)RealtimeTransportState.Failed);
            _lastError = error;
        }

        _receiveQueue.Writer.TryComplete();
        _lifetimeCts.Cancel();
        StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Failed, error));
    }

    private async Task CloseCoreAsync(CancellationToken cancellationToken)
    {
        bool notifyClosed;
        lock (_sync)
        {
            Interlocked.Exchange(ref _closing, 1);
            notifyClosed = State != RealtimeTransportState.Closed;
            Volatile.Write(ref _state, (int)RealtimeTransportState.Closed);
        }

        _lifetimeCts.Cancel();
        await SendByeWithinGraceAsync(cancellationToken).ConfigureAwait(false);
        await AwaitPumpAsync(_sendPumpTask).ConfigureAwait(false);
        await AwaitPumpAsync(_controlPumpTask).ConfigureAwait(false);

        _peerConnection.OnRtpPacketReceived -= OnRtpPacketReceived;
        _peerConnection.onconnectionstatechange -= OnConnectionStateChanged;
        await _signalingConnection.DisposeAsync().ConfigureAwait(false);

        _receiveQueue.Writer.TryComplete();
        _sendQueue.Writer.TryComplete();
        _peerConnection.Close("monitoring session closed");
        _peerConnection.Dispose();
        _lifetimeCts.Dispose();

        if (notifyClosed)
        {
            StateChanged?.Invoke(this, new RealtimeTransportStateChangedEventArgs(RealtimeTransportState.Closed));
        }
    }

    private async Task SendByeWithinGraceAsync(CancellationToken cancellationToken)
    {
        if (_signalingConnection.IsClosed)
        {
            return;
        }

        using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        graceCts.CancelAfter(GracefulCloseTimeout);
        try
        {
            await _signalingConnection.SendByeAsync(graceCts.Token).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is OperationCanceledException or IOException or ObjectDisposedException)
        {
            // Токен ограничивает только ожидание graceful shutdown; локальное закрытие продолжается.
        }
    }

    private static async Task AwaitPumpAsync(Task pumpTask)
    {
        try
        {
            await pumpTask.WaitAsync(PumpShutdownTimeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // Фоновый pump не завершился за отведённое время: сокеты освобождаются принудительно.
        }
        catch (OperationCanceledException)
        {
            // Pump завершился по lifetime-отмене.
        }
    }
}
