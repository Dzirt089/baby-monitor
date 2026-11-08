namespace BabyMonitor.App.Presentation;

/// <summary>Итог установки APK для отображения в UI (без PII и секретов).</summary>
public enum UpdateInstallOutcome
{
    /// <summary>Установка подтверждена системой.</summary>
    Succeeded = 0,

    /// <summary>Пользователь отменил установку.</summary>
    Cancelled = 1,

    /// <summary>Установка завершилась ошибкой (или не удалось показать подтверждение).</summary>
    Failed = 2
}

/// <summary>Аргументы события итога установки APK.</summary>
public sealed class UpdateInstallOutcomeEventArgs : EventArgs
{
    /// <summary>Создаёт аргументы события.</summary>
    /// <param name="outcome">Итог установки.</param>
    public UpdateInstallOutcomeEventArgs(UpdateInstallOutcome outcome) => Outcome = outcome;

    /// <summary>Итог установки.</summary>
    public UpdateInstallOutcome Outcome { get; }
}

/// <summary>
/// Порт чтения/наблюдения итога установки APK: Platform-адаптер (PackageInstaller receiver +
/// SharedPreferences) публикует итог; ViewModel не зависит от Android types.
/// </summary>
public interface IUpdateInstallStatusSource
{
    /// <summary>Live-итог установки изменился (может подниматься с фонового потока).</summary>
    event EventHandler<UpdateInstallOutcomeEventArgs>? OutcomeChanged;

    /// <summary>
    /// Забирает persisted-итог последней установки одноразово либо null. Нужен после перезапуска
    /// процесса, когда системный диалог подтверждения завершился без открытого экрана обновлений.
    /// </summary>
    /// <returns>Итог установки либо <see langword="null"/>.</returns>
    UpdateInstallOutcome? TakeLastOutcome();
}
