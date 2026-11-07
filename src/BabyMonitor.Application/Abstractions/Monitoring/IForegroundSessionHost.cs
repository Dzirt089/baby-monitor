using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>
/// Порт foreground-сессии мониторинга: запуск/остановка Android foreground service с постоянным
/// уведомлением (роль, состояние, действие Stop) и наблюдение immutable снимков состояния.
/// </summary>
/// <remarks>
/// StartAsync разрешён только при видимой Activity (явное действие пользователя); adapter обязан
/// проверить это и вернуть <see cref="ForegroundSessionErrorCodes.BackgroundStartDenied"/>.
/// Для роли Child StartAsync также требует RECORD_AUDIO и возвращает
/// <see cref="ForegroundSessionErrorCodes.MicrophonePermissionDenied"/> при отказе.
/// После StartAsync service продолжает работу при уничтожении Activity; StopAsync останавливает
/// service и убирает уведомление. Роль Parent не открывает микрофон, роль Child не открывает playback.
/// </remarks>
public interface IForegroundSessionHost
{
    /// <summary>
    /// Последний известный снимок foreground-сессии; null только до первого запуска.
    /// После Stop возвращается терминальный снимок Stopped (не null).
    /// </summary>
    ForegroundSessionSnapshot? Current { get; }

    /// <summary>Уведомление об изменении снимка foreground-сессии.</summary>
    event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает foreground service для указанной роли. Если сессия уже запущена с той же ролью —
    /// идемпотентный успех; с другой ролью — <see cref="ForegroundSessionErrorCodes.AlreadyRunning"/>.
    /// </summary>
    /// <param name="role">Роль Child или Parent.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Снимок состояния Running либо типизированная ошибка.</returns>
    Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken);

    /// <summary>
    /// Останавливает foreground service и убирает уведомление. Идемпотентно для остановленной
    /// сессии (повторный Stop возвращает NotRunning, если снимка нет).
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания остановки.</param>
    /// <returns>Снимок состояния Stopped либо типизированная ошибка.</returns>
    Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Возвращает текущий снимок foreground-сессии. NotRunning возвращается только до первого
    /// запуска; после Stop возвращается терминальный снимок Stopped.
    /// </summary>
    /// <param name="cancellationToken">Отмена запроса.</param>
    /// <returns>Снимок либо ошибка.</returns>
    Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken);
}
