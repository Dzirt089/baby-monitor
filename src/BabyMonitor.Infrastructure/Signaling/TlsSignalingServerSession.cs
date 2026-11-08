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
    /// Ожидает одно доверенное подключение и выполняет TLS + identity assertion. После успеха
    /// listener больше не принимает соединения.
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

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.HandshakeTimeout);

            TcpClient? tcpClient = null;
            try
            {
                tcpClient = await _listener.AcceptTcpClientAsync(timeoutCts.Token).ConfigureAwait(false);
                tcpClient.NoDelay = true;

                var handshakeResult = await SignalingHandshake
                    .AcceptAsync(tcpClient, _identity, _options, timeoutCts.Token)
                    .ConfigureAwait(false);
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return Result<TlsSignalingConnection>.Failure(SignalingErrors.SignalingTimeout());
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
