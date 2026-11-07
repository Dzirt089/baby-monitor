namespace BabyMonitor.Platform.Android.ForegroundServices;

/// <summary>
/// Видимость текущей Activity. Нужна, чтобы не запускать foreground service из фона:
/// Android требует явного действия пользователя при видимой Activity.
/// </summary>
public interface IActivityVisibility
{
    /// <summary>true, если Activity сейчас видима (resumed).</summary>
    bool IsVisible { get; }
}
