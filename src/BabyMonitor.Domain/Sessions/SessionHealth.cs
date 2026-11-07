namespace BabyMonitor.Domain.Sessions;

/// <summary>Состояние control-канала.</summary>
public enum SessionChannelState
{
    /// <summary>Состояние ещё не измерялось.</summary>
    Unknown = 0,

    /// <summary>Канал здоров.</summary>
    Connected = 1,

    /// <summary>Канал деградирован (например, heartbeat задерживается).</summary>
    Degraded = 2,

    /// <summary>Канал потерян.</summary>
    Lost = 3
}

/// <summary>Состояние media flow (не заменяет control channel).</summary>
public enum AudioFlowState
{
    /// <summary>Media flow не активен.</summary>
    Inactive = 0,

    /// <summary>Media flow подтверждён.</summary>
    Flowing = 1,

    /// <summary>Media flow остановился (underflow/silence/нет пакетов).</summary>
    Stalled = 2
}

/// <summary>
/// Immutable health-снимок сеанса: control channel, media flow, capture/playback активность.
/// «Transport connected» не равен «пользователь слышит звук»: истина только при Media=Flowing.
/// </summary>
public sealed record SessionHealth
{
    /// <summary>Создаёт health-снимок.</summary>
    /// <param name="control">Состояние control-канала.</param>
    /// <param name="media">Состояние media flow.</param>
    /// <param name="captureActive">Активен ли захват (только Child).</param>
    /// <param name="playbackActive">Активно ли воспроизведение (только Parent).</param>
    public SessionHealth(SessionChannelState control, AudioFlowState media, bool captureActive, bool playbackActive)
    {
        Control = control;
        Media = media;
        CaptureActive = captureActive;
        PlaybackActive = playbackActive;
    }

    /// <summary>Начальный health-снимок до измерений.</summary>
    public static SessionHealth Initial { get; } = new(SessionChannelState.Unknown, AudioFlowState.Inactive, false, false);

    /// <summary>Состояние control-канала.</summary>
    public SessionChannelState Control { get; }

    /// <summary>Состояние media flow.</summary>
    public AudioFlowState Media { get; }

    /// <summary>Захват активен (истинно только для Child).</summary>
    public bool CaptureActive { get; }

    /// <summary>Воспроизведение активно (истинно только для Parent).</summary>
    public bool PlaybackActive { get; }

    /// <summary>Пользователь должен слышать звук: control здоров и media flow подтверждён.</summary>
    public bool IsUserHearingAudio => Control is SessionChannelState.Connected && Media is AudioFlowState.Flowing;
}
