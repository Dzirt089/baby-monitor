using System.Net;
using System.Net.Sockets;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Активное прослушивание signaling-порта (Child). Принимает одно доверенное подключение:
/// после успешного <see cref="AcceptAsync"/> повторный вызов возвращает invalid_state. Отказ peer-а
/// (недоверенная identity, malformed кадр, timeout) возвращается типизированной ошибкой и не
/// закрывает listener — Application/reconnect-политика решает, продолжать ли ожидание.
/// </summary>
/// <remarks>
/// Ожидание TCP-подключения ограничено только отменой вызывающего токена/Dispose: Child обязан
/// оставаться в ожидании Parent (Advertising) произвольно долго. <see cref="SignalingOptions.HandshakeTimeout"/>
/// применяется только к TLS handshake и identity assertion уже подключившегося peer-а.
/// </remarks>
public sealed class TlsSignalingServerSession : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly SignalingIdentity _identity;
    private readonly SignalingOptions _options;
    private readonly SemaphoreSlim _acceptLock = new(1, 1);
    private int _accepted;
    private int _disposed;

    internal TlsSignalingServerSession(
        TcpListener listener,
        int boundPort,
        SignalingIdentity identity,
        SignalingOptions options)
    {
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        _listener = listener;
        BoundPort = boundPort;
        _identity = identity;
        _options = options;
    }

    /// <summary>Фактический локальный порт signaling (публикуется через NSD).</summary>
    public int BoundPort { get; }

    /// <summary>
    /// Ожидает одно доверенное подключение и выполняет TLS + identity assertion. Ожидание TCP peer-а
    /// не ограничено <see cref="SignalingOptions.HandshakeTimeout"/> (только отмена/Dispose); таймаут
    /// применяется к TLS+hello после TCP-подключения. После успеха listener больше не принимает
    /// соединения.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания.</param>
    /// <returns>Аутентифицированное signaling-соединение либо типизированная ошибка.</returns>
    public Task<Result<TlsSignalingConnection>> AcceptAsync(CancellationToken cancellationToken) =>
        AcceptCoreAsync(markAccepted: true, cancellationToken);

    /// <summary>
    /// Принимает соединение, но не «расходует» listener (внутренний путь media-адаптера: после
    /// отказа media-handshake listener остаётся пригодным для повтора). Успех фиксируется
    /// вызывающим через <see cref="MarkAccepted"/> только после полной media-сессии.
    /// </summary>
    internal Task<Result<TlsSignalingConnection>> AcceptUncommittedAsync(CancellationToken cancellationToken) =>
        AcceptCoreAsync(markAccepted: false, cancellationToken);

    /// <summary>Фиксирует единственное успешное принятое соединение (после media-handshake).</summary>
    internal void MarkAccepted() => Interlocked.Exchange(ref _accepted, 1);

    private async Task<Result<TlsSignalingConnection>> AcceptCoreAsync(
        bool markAccepted,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _acceptLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Volatile.Read(ref _disposed) == 1 || Volatile.Read(ref _accepted) == 1)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.InvalidState());
            }

            TcpClient? tcpClient = null;
            try
            {
                // Ожидание TCP-подключения не ограничено HandshakeTimeout: Child ждёт Parent в
                // Advertising-состоянии, пока вызывающий не отменит ожидание или listener не остановят.
                tcpClient = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                tcpClient.NoDelay = true;

                // HandshakeTimeout применяется только к TLS + hello/identity assertion уже
                // подключившегося peer-а (TCP accepted).
                using var handshakeCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                handshakeCts.CancelAfter(_options.HandshakeTimeout);

                Result<TlsSignalingConnection> handshakeResult;
                try
                {
                    handshakeResult = await SignalingHandshake
                        .AcceptAsync(tcpClient, _identity, _options, handshakeCts.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Result<TlsSignalingConnection>.Failure(SignalingErrors.SignalingTimeout());
                }

                if (handshakeResult.IsSuccess)
                {
                    // Владение сокетом перешло signaling-соединению. Listener «расходуется» либо
                    // сразу (signaling-контракт), либо вызывающим после полной media-сессии.
                    tcpClient = null;
                    if (markAccepted)
                    {
                        MarkAccepted();
                    }
                }

                return handshakeResult;
            }
            catch (OperationCanceledException)
            {
                // Отмена вызывающего токена: контракт AcceptAsync — OperationCanceledException.
                throw;
            }
            catch (ObjectDisposedException)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.InvalidState());
            }
            catch (SocketException) when (cancellationToken.IsCancellationRequested || Volatile.Read(ref _disposed) == 1)
            {
                // Остановка listener-а во время ожидания: для вызывающего это invalid_state,
                // а не недоступность peer-а.
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.InvalidState());
            }
            catch (SocketException)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.PeerUnavailable());
            }
            catch (IOException)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.TlsFailure());
            }
            finally
            {
                tcpClient?.Dispose();
            }
        }
        finally
        {
            _acceptLock.Release();
        }
    }

    /// <summary>Останавливает прослушивание и освобождает порт; безопасно вызывать повторно.</summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Порт уже освобождён: повторная остановка listener-а не является ошибкой.
        }

        // Семафор сериализации Accept не освобождается: SemaphoreSlim без WaitHandle не требует
        // Dispose, а его освобождение гонялось бы с уже ожидающим AcceptAsync.
        return ValueTask.CompletedTask;
    }
}
