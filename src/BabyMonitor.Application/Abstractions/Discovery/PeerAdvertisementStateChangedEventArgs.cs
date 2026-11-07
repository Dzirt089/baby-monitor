using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>Снимок изменения состояния advertise-сессии.</summary>
public sealed class PeerAdvertisementStateChangedEventArgs : EventArgs
{
    /// <summary>Создаёт событие изменения состояния advertise.</summary>
    /// <param name="state">Новое состояние.</param>
    /// <param name="error">Ошибка при переходе в Failed.</param>
    public PeerAdvertisementStateChangedEventArgs(PeerAdvertisementState state, Error? error = null)
    {
        State = state;
        Error = error;
    }

    /// <summary>Новое состояние advertise.</summary>
    public PeerAdvertisementState State { get; }

    /// <summary>Типизированная ошибка при переходе в Failed.</summary>
    public Error? Error { get; }
}
