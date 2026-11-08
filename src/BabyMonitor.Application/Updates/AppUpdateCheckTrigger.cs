namespace BabyMonitor.Application.Updates;

/// <summary>Причина проверки обновлений: ручное действие пользователя или фоновая автопроверка.</summary>
public enum AppUpdateCheckTrigger
{
    /// <summary>Пользователь явно нажал «Проверить обновления»; throttle/disabled игнорируются.</summary>
    Manual = 0,

    /// <summary>Автопроверка при запуске: учитывает disabled и суточный throttle.</summary>
    Automatic = 1
}
