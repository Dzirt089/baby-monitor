using System.Net;
using System.Net.Sockets;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Слушатель signaling-соединений (Child): открывает TCP-порт в LAN, принимает одно доверенное
/// подключение за раз и выполняет TLS 1.2+ handshake с mutual authentication и identity assertion
/// (ADR-0007). Порт не является identity; недоверенный peer отклоняется до обмена SDP.
/// </summary>
public sealed class TlsSignalingServer
{
    private readonly SignalingIdentity _identity;
    private readonly SignalingOptions _options;

    /// <summary>Создаёт слушатель signaling.</summary>
    /// <param name="identity">Локальная identity, trusted store и ожидаемый fingerprint peer.</param>
    /// <param name="options">Настройки signaling; по умолчанию стандартные.</param>
    /// <exception cref="ArgumentNullException">Аргумент равен <see langword="null"/>.</exception>
    public TlsSignalingServer(SignalingIdentity identity, SignalingOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(identity);
        _identity = identity;
        _options = options ?? new SignalingOptions();
    }

    /// <summary>
    /// Запускает прослушивание TCP-порта.
    /// </summary>
    /// <param name="port">Порт 0..65535; 0 — выбрать свободный порт средствами ОС.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Сессия ожидания подключения либо типизированная ошибка.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Порт вне диапазона 0..65535.</exception>
    public Task<Result<TlsSignalingServerSession>> StartAsync(int port, CancellationToken cancellationToken)
    {
        if (port is < 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Порт должен быть в диапазоне 0..65535.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            var boundPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            var session = new TlsSignalingServerSession(listener, boundPort, _identity, _options);
            return Task.FromResult(Result<TlsSignalingServerSession>.Success(session));
        }
        catch (SocketException)
        {
            listener?.Stop();
            return Task.FromResult(Result<TlsSignalingServerSession>.Failure(SignalingErrors.PeerUnavailable()));
        }
    }
}
