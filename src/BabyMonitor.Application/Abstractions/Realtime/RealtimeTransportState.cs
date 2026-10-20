namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>Состояние transport-соединения. Не заменяет SessionHealth: только transport-уровень.</summary>
public enum RealtimeTransportState
{
    /// <summary>Идёт установка соединения.</summary>
    Connecting = 0,

    /// <summary>Transport-соединение установлено; media flow ещё не подтверждён.</summary>
    Connected = 1,

    /// <summary>Соединение завершилось ошибкой.</summary>
    Failed = 2,

    /// <summary>Соединение закрыто локально; повторное открытие запрещено.</summary>
    Closed = 3
}
