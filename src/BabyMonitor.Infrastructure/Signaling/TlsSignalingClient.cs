using System.Net.Sockets;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Инициатор signaling-соединения (Parent): TCP connect в LAN, TLS 1.2+ с mutual authentication
/// эфемерными сертификатами, затем app-layer identity assertion и pinning по trusted store
/// (ADR-0007). Cleartext и отключение проверки TLS не используются; endpoint — только адрес.
/// </summary>
public sealed class TlsSignalingClient
{
    private readonly TransportEndpoint _endpoint;
    private readonly SignalingIdentity _identity;
    private readonly SignalingOptions _options;

    /// <summary>Создаёт клиент signaling.</summary>
    /// <param name="endpoint">Адрес peer (получен из discovery); не является identity.</param>
    /// <param name="identity">Локальная identity, trusted store и ожидаемый fingerprint peer.</param>
    /// <param name="options">Настройки signaling; по умолчанию стандартные.</param>
    /// <exception cref="ArgumentNullException">Аргумент равен <see langword="null"/>.</exception>
    public TlsSignalingClient(
        TransportEndpoint endpoint,
        SignalingIdentity identity,
        SignalingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(identity);

        _endpoint = endpoint;
        _identity = identity;
        _options = options ?? new SignalingOptions();
    }

    /// <summary>
    /// Устанавливает TLS signaling-соединение и проверяет identity peer. Ожидаемые отказы
    /// возвращаются типизированной ошибкой; отмена caller-токена — <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="cancellationToken">Отмена попытки соединения.</param>
    /// <returns>Аутентифицированное соединение либо типизированная ошибка.</returns>
    public async Task<Result<TlsSignalingConnection>> ConnectAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Предварительная проверка identity/доверия до открытия сокета: недоверенный peer
        // отклоняется без сетевого обмена.
        var localIdentityResult = await _identity.GetLocalIdentityAsync(cancellationToken).ConfigureAwait(false);
        if (!localIdentityResult.IsSuccess)
        {
            return Result<TlsSignalingConnection>.Failure(localIdentityResult.Error!);
        }

        var expectedPeerResult = await _identity.ResolveTrustedPeerAsync(cancellationToken).ConfigureAwait(false);
        if (!expectedPeerResult.IsSuccess)
        {
            return Result<TlsSignalingConnection>.Failure(expectedPeerResult.Error!);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.HandshakeTimeout);
        TcpClient? tcpClient = null;
        try
        {
            tcpClient = new TcpClient { NoDelay = true };
            await tcpClient.ConnectAsync(_endpoint.Host, _endpoint.Port, timeoutCts.Token).ConfigureAwait(false);

            var handshakeResult = await SignalingHandshake
                .ConnectAsync(tcpClient, _identity, _options, timeoutCts.Token)
                .ConfigureAwait(false);
            if (handshakeResult.IsSuccess)
            {
                // Владение сокетом перешло signaling-соединению.
                tcpClient = null;
            }

            return handshakeResult;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result<TlsSignalingConnection>.Failure(SignalingErrors.SignalingTimeout());
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
}
