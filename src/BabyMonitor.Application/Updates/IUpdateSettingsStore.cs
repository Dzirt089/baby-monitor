namespace BabyMonitor.Application.Updates;

/// <summary>
/// Порт сохранённых настроек автообновления (SharedPreferences в Platform.Android):
/// включена ли автопроверка и время последней успешной проверки (для суточного throttle).
/// </summary>
public interface IUpdateSettingsStore
{
    /// <summary>Включена ли автоматическая проверка обновлений (по умолчанию — настройка UI).</summary>
    bool AutoCheckEnabled { get; set; }

    /// <summary>Время последней успешной проверки; null, если проверок ещё не было.</summary>
    DateTimeOffset? LastSuccessfulCheckAt { get; set; }
}
