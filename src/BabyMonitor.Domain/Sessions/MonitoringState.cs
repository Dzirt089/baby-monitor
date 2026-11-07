namespace BabyMonitor.Domain.Sessions;

/// <summary>
/// Состояние сеанса наблюдения (единый КА; не заменяется набором bool).
/// </summary>
public enum MonitoringState
{
    /// <summary>Сеанс создан, но ещё не начал discovery/advertising; после Stop — терминален.</summary>
    Idle = 0,

    /// <summary>Parent ищет Child в локальной сети.</summary>
    Discovering = 1,

    /// <summary>Child публикует сервис в локальной сети.</summary>
    Advertising = 2,

    /// <summary>Выполняется сопряжение.</summary>
    Pairing = 3,

    /// <summary>Устанавливается transport-соединение.</summary>
    Connecting = 4,

    /// <summary>Transport-соединение установлено; media flow ещё не подтверждён.</summary>
    Connected = 5,

    /// <summary>Media flow подтверждён (пользователь должен слышать звук).</summary>
    MediaFlowing = 6,

    /// <summary>Control/media деградировали (heartbeat или media flow), но сеанс ещё можно восстановить.</summary>
    Degraded = 7,

    /// <summary>Идёт автоматическое переподключение.</summary>
    Reconnecting = 8,

    /// <summary>Сеанс завершился ошибкой; терминальное состояние.</summary>
    Failed = 9,

    /// <summary>Идёт ручная остановка.</summary>
    Stopping = 10
}
