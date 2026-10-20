namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>Снимок изменения состояния transport-соединения.</summary>
public sealed class RealtimeTransportStateChangedEventArgs : EventArgs
{
    /// <summary>Создаёт событие изменения состояния.</summary>
    /// <param name="state">Новое состояние.</param>
    /// <param name="error">Ошибка для состояния <see cref="RealtimeTransportState.Failed"/>; иначе <see langword="null"/>.</param>
    public RealtimeTransportStateChangedEventArgs(RealtimeTransportState state, Results.Error? error = null)
    {
        State = state;
        Error = error;
    }

    /// <summary>Новое состояние transport-соединения.</summary>
    public RealtimeTransportState State { get; }

    /// <summary>Типизированная ошибка при переходе в <see cref="RealtimeTransportState.Failed"/>.</summary>
    public Results.Error? Error { get; }
}
