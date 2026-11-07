namespace BabyMonitor.Application.Abstractions.Discovery;

/// <summary>Состояние advertise-сессии (публикация Child в mDNS).</summary>
public enum PeerAdvertisementState
{
    /// <summary>Идёт регистрация сервиса.</summary>
    Starting = 0,

    /// <summary>Сервис опубликован и доступен для обнаружения.</summary>
    Advertising = 1,

    /// <summary>Публикация завершилась ошибкой.</summary>
    Failed = 2,

    /// <summary>Публикация остановлена локально; сессия терминальна.</summary>
    Stopped = 3
}
