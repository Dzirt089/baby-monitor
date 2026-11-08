using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Media;

/// <summary>
/// Порт фабрики media-сеанса: composition root создаёт все адаптеры для указанной роли
/// (transport/listener с pinning ожидаемой identity из trusted store, audio, codec, discovery,
/// connectivity) и собирает <see cref="MonitoringMediaSession"/>. Используется foreground service.
/// </summary>
public interface IMonitoringMediaSessionFactory
{
    /// <summary>
    /// Создаёт media-сеанс для роли. Если для роли нет доверенного peer — typed
    /// <see cref="MediaSessionErrorCodes.NoTrustedPeer"/>.
    /// </summary>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <param name="cancellationToken">Отмена создания (trusted store/identity).</param>
    /// <returns>Сеанс либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Создание отменено.</exception>
    Task<Result<MonitoringMediaSession>> CreateAsync(DeviceRole role, CancellationToken cancellationToken);
}

/// <summary>
/// Composition root приложения (Android Application), доступный foreground service:
/// отдаёт фабрику media-сеансов, чтобы service не собирал зависимости сам (AGENTS.md §5).
/// </summary>
public interface IMonitoringMediaSessionFactoryProvider
{
    /// <summary>Фабрика media-сеансов приложения.</summary>
    IMonitoringMediaSessionFactory MediaSessions { get; }
}
