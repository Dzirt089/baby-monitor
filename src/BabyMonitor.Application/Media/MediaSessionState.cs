namespace BabyMonitor.Application.Media;

/// <summary>
/// Presentation-agnostic состояние media-сеанса наблюдения (единый КА координатора).
/// Не заменяется набором bool; переходы выполняет <see cref="MonitoringMediaCoordinator"/>.
/// </summary>
public enum MediaSessionState
{
    /// <summary>Координатор создан, сеанс ещё не запускался.</summary>
    Idle = 0,

    /// <summary>Сеанс запускается: подготавливаются порты и ресурсы.</summary>
    Starting = 1,

    /// <summary>Child публикует сервис и ожидает доверенный peer (media ещё не течёт).</summary>
    Advertising = 2,

    /// <summary>Parent ищет детское устройство в локальной сети.</summary>
    Discovering = 3,

    /// <summary>Устанавливается transport-соединение (TLS/signaling/DTLS).</summary>
    Connecting = 4,

    /// <summary>Transport-соединение установлено, но media flow ещё не подтверждён.</summary>
    Connected = 5,

    /// <summary>
    /// Media flow подтверждён: Child отправляет, Parent принимает и воспроизводит кадры.
    /// «Transport connected» без этого состояния не означает, что пользователь слышит звук.
    /// </summary>
    MediaFlowing = 6,

    /// <summary>Сеанс деградировал (transport/media потеряны), переподключение ещё возможно.</summary>
    Degraded = 7,

    /// <summary>Идёт ограниченное автоматическое переподключение по ReconnectPolicy.</summary>
    Reconnecting = 8,

    /// <summary>Идёт ручная остановка: ресурсы освобождаются, аудио уже не передаётся.</summary>
    Stopping = 9,

    /// <summary>Сеанс остановлен локально; терминальное состояние.</summary>
    Stopped = 10,

    /// <summary>Сеанс завершился типизированной ошибкой; терминальное состояние.</summary>
    Failed = 11
}
