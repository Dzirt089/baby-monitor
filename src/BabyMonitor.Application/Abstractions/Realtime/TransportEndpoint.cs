namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Адрес peer для установки transport-соединения. Не является identity устройства:
/// при смене сети или IP endpoint резолвится заново, а доверие проверяется pairing-слоем.
/// </summary>
public sealed record TransportEndpoint
{
    /// <summary>Создаёт endpoint для transport-соединения.</summary>
    /// <param name="host">Хост или IP-адрес, полученный из discovery/diagnostics.</param>
    /// <param name="port">Порт transport-сервиса.</param>
    /// <exception cref="ArgumentException">Хост пуст.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Порт вне диапазона 1..65535.</exception>
    public TransportEndpoint(string host, int port)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        if (port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Порт должен быть в диапазоне 1..65535.");
        }

        Host = host;
        Port = port;
    }

    /// <summary>Хост или IP-адрес peer.</summary>
    public string Host { get; }

    /// <summary>Порт transport-сервиса.</summary>
    public int Port { get; }
}
