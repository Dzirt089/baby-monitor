using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>Снимок изменения состояния discovery-сессии.</summary>
public sealed class PeerDiscoveryStateChangedEventArgs : EventArgs
{
    /// <summary>Создаёт событие изменения состояния discovery.</summary>
    /// <param name="state">Новое состояние.</param>
    /// <param name="error">Ошибка при переходе в <see cref="PeerDiscoveryState.Failed"/>.</param>
    public PeerDiscoveryStateChangedEventArgs(PeerDiscoveryState state, Error? error = null)
    {
        State = state;
        Error = error;
    }

    /// <summary>Новое состояние discovery.</summary>
    public PeerDiscoveryState State { get; }

    /// <summary>Типизированная ошибка при переходе в Failed.</summary>
    public Error? Error { get; }
}
