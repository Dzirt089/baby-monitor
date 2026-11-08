using BabyMonitor.Application.Media;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>
/// Immutable снимок foreground-сессии: роль, состояние и (для Failed) типизированная ошибка.
/// Не содержит View/Activity/Service ссылок и аудиоданных.
/// </summary>
public sealed record ForegroundSessionSnapshot
{
    /// <summary>Создаёт снимок foreground-сессии.</summary>
    /// <param name="role">Роль устройства; Child или Parent.</param>
    /// <param name="state">Состояние foreground-сессии.</param>
    /// <param name="error">Ошибка только для состояния Failed.</param>
    /// <param name="media">Снимок media-сеанса (005 phase 2) либо null, если media ещё не запускалась.</param>
    /// <exception cref="ArgumentException">
    /// Роль Unknown, Error передан для не-Failed состояния или media.Role не совпадает с role.
    /// </exception>
    /// <exception cref="ArgumentNullException">Для Failed не передана ошибка.</exception>
    public ForegroundSessionSnapshot(
        DeviceRole role,
        MonitoringForegroundState state,
        Error? error = null,
        MediaSessionSnapshot? media = null)
    {
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Foreground-сессия требует роль Child или Parent.", nameof(role));
        }

        if (state is MonitoringForegroundState.Failed)
        {
            ArgumentNullException.ThrowIfNull(error, nameof(error));
        }
        else if (error is not null)
        {
            throw new ArgumentException("Error допустим только для состояния Failed.", nameof(error));
        }

        if (media is not null && media.Role != role)
        {
            throw new ArgumentException("Media-снимок должен относиться к той же роли.", nameof(media));
        }

        Role = role;
        State = state;
        Error = error;
        Media = media;
    }

    /// <summary>Роль устройства (Child или Parent).</summary>
    public DeviceRole Role { get; }

    /// <summary>Состояние foreground-сессии.</summary>
    public MonitoringForegroundState State { get; }

    /// <summary>Типизированная ошибка для состояния Failed.</summary>
    public Error? Error { get; }

    /// <summary>
    /// Снимок media-сеанса (005 phase 2): состояние signaling/media/audio без аудиоданных и
    /// секретов. null, если media ещё не запускалась (например, до v0.3.0-интеграции).
    /// </summary>
    public MediaSessionSnapshot? Media { get; }

    /// <summary>Терминальное ли состояние (Stopped или Failed).</summary>
    public bool IsTerminal => State is MonitoringForegroundState.Stopped or MonitoringForegroundState.Failed;
}
