using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Realtime;

/// <summary>
/// Активное ожидание одного доверенного peer (Child) за
/// <see cref="IRealtimeTransportListenerSession"/>. После успешного Accept возвращает media-сессию;
/// повторный Accept — invalid_state. Отказ (недоверенный peer, malformed signaling, timeout)
/// возвращается типизированной ошибкой, listener остаётся пригодным для повтора.
/// </summary>
public sealed class SipsorceryRealtimeTransportListenerSession : IRealtimeTransportListenerSession
{
    private readonly TlsSignalingServerSession _serverSession;
    private readonly SipsorceryTransportOptions _options;
    private readonly SemaphoreSlim _acceptLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private int _accepted;
    private int _disposed;

    internal SipsorceryRealtimeTransportListenerSession(
        TlsSignalingServerSession serverSession,
        SipsorceryTransportOptions options)
    {
        ArgumentNullException.ThrowIfNull(serverSession);
        ArgumentNullException.ThrowIfNull(options);

        _serverSession = serverSession;
        _options = options;
    }

    /// <inheritdoc />
    public int BoundPort => _serverSession.BoundPort;

    /// <inheritdoc />
    public async Task<Result<IRealtimeTransportSession>> AcceptAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _acceptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _accepted) == 1)
            {
                return Result<IRealtimeTransportSession>.Failure(SignalingErrors.InvalidState());
            }

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            try
            {
                var signalingResult = await _serverSession.AcceptUncommittedAsync(linkedCts.Token).ConfigureAwait(false);
                if (!signalingResult.IsSuccess)
                {
                    return Result<IRealtimeTransportSession>.Failure(signalingResult.Error!);
                }

                var signalingConnection = signalingResult.Value;
                try
                {
                    var mediaResult = await SipsorceryMediaHandshake
                        .CreateAnswerAsync(signalingConnection, _options, linkedCts.Token)
                        .ConfigureAwait(false);
                    if (!mediaResult.IsSuccess)
                    {
                        // Соединение аутентифицировано, но media не поднялся: listener остаётся
                        // пригодным для нового доверенного peer.
                        await signalingConnection.DisposeAsync().ConfigureAwait(false);
                        return Result<IRealtimeTransportSession>.Failure(mediaResult.Error!);
                    }

                    _serverSession.MarkAccepted();
                    Interlocked.Exchange(ref _accepted, 1);
                    var session = new SipsorceryRealtimeTransportSession(signalingConnection, mediaResult.Value, _options);
                    return Result<IRealtimeTransportSession>.Success(session);
                }
                catch
                {
                    await signalingConnection.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Dispose слушателя отменил незавершённое ожидание: состояние InvalidState, не throw.
                return Result<IRealtimeTransportSession>.Failure(SignalingErrors.InvalidState());
            }
        }
        finally
        {
            _acceptLock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _lifetime.CancelAsync();
        // Дожидаемся завершения незавершённого AcceptAsync (он отменён выше), затем освобождаем
        // signaling listener и lifetime-источник.
        await _acceptLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await _serverSession.DisposeAsync().ConfigureAwait(false);
            _lifetime.Dispose();
        }
        finally
        {
            _acceptLock.Release();
        }
    }
}
