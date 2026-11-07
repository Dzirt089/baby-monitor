namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>Состояние discovery-сессии (поиск Child в локальной сети).</summary>
public enum PeerDiscoveryState
{
    /// <summary>Идёт запуск discovery.</summary>
    Starting = 0,

    /// <summary>Discovery активно; peer-ы могут появляться.</summary>
    Discovering = 1,

    /// <summary>Discovery завершилось ошибкой.</summary>
    Failed = 2,

    /// <summary>Discovery остановлено локально; сессия терминальна.</summary>
    Stopped = 3
}
