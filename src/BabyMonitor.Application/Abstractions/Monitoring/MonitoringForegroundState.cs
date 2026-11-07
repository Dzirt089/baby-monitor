namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>Состояние foreground-сессии мониторинга.</summary>
public enum MonitoringForegroundState
{
    /// <summary>Service запускается, уведомление ещё не показано.</summary>
    Starting = 0,

    /// <summary>Service работает в foreground с постоянным уведомлением.</summary>
    Running = 1,

    /// <summary>Service останавливается (в 003 не производится; зарезервировано для 005/006).</summary>
    Stopping = 2,

    /// <summary>Service остановлен; терминальное состояние.</summary>
    Stopped = 3,

    /// <summary>Service завершился ошибкой.</summary>
    Failed = 4
}
