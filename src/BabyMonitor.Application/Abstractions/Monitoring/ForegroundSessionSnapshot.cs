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
    /// <exception cref="ArgumentException">Роль Unknown или Error передан для не-Failed состояния.</exception>
    /// <exception cref="ArgumentNullException">Для Failed не передана ошибка.</exception>
    public ForegroundSessionSnapshot(DeviceRole role, MonitoringForegroundState state, Error? error = null)
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

        Role = role;
        State = state;
        Error = error;
    }

    /// <summary>Роль устройства (Child или Parent).</summary>
    public DeviceRole Role { get; }

    /// <summary>Состояние foreground-сессии.</summary>
    public MonitoringForegroundState State { get; }

    /// <summary>Типизированная ошибка для состояния Failed.</summary>
    public Error? Error { get; }

    /// <summary>Терминальное ли состояние (Stopped или Failed).</summary>
    public bool IsTerminal => State is MonitoringForegroundState.Stopped or MonitoringForegroundState.Failed;
}
