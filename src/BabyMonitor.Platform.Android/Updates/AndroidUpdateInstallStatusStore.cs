using Android.Content;
using Android.Util;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Persisted-статус последней установки APK (SharedPreferences): переживает смерть процесса,
/// пока системный диалог подтверждения установки завершается. Без PII и секретов; UI забирает
/// статус один раз (<see cref="TakeLastStatus"/>) и очищает запись, чтобы не показывать его повторно.
/// </summary>
public sealed class AndroidUpdateInstallStatusStore
{
    private const string PreferencesName = "babymonitor_updates";
    private const string LastStatusKey = "last_install_status";
    private const string LastStatusAtKey = "last_install_status_at_unix_ms";
    private const string LogTag = "BabyMonitor.Updates";

    private readonly object _sync = new();
    private readonly ISharedPreferences? _preferences;
    private readonly TimeProvider _timeProvider;

    /// <summary>Создаёт хранилище статуса установки.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <param name="timeProvider">Источник времени; по умолчанию <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public AndroidUpdateInstallStatusStore(Context context, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext ?? context;
        _preferences = applicationContext.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Сохраняет статус установки (UTC-время фиксируется TimeProvider-ом).</summary>
    /// <param name="status">Статус установки.</param>
    public void Save(UpdateInstallStatus status)
    {
        lock (_sync)
        {
            var editor = _preferences?.Edit();
            if (editor is null)
            {
                return;
            }

            editor.PutInt(LastStatusKey, (int)status);
            editor.PutLong(LastStatusAtKey, _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());
            editor.Apply();
        }
    }

    /// <summary>
    /// Возвращает последний сохранённый статус и очищает запись (одноразовое отображение в UI).
    /// </summary>
    /// <returns>Снимок статуса либо <see langword="null"/>, если статуса нет/он повреждён.</returns>
    public UpdateInstallStatusSnapshot? TakeLastStatus()
    {
        lock (_sync)
        {
            if (_preferences is null)
            {
                return null;
            }

            var statusValue = _preferences.GetInt(LastStatusKey, 0);
            var recordedAtMilliseconds = _preferences.GetLong(LastStatusAtKey, -1);
            var editor = _preferences.Edit();
            editor?.Remove(LastStatusKey);
            editor?.Remove(LastStatusAtKey);
            editor?.Apply();

            if (!Enum.IsDefined(typeof(UpdateInstallStatus), statusValue) || recordedAtMilliseconds <= 0)
            {
                return null;
            }

            try
            {
                return new UpdateInstallStatusSnapshot(
                    (UpdateInstallStatus)statusValue,
                    DateTimeOffset.FromUnixTimeMilliseconds(recordedAtMilliseconds));
            }
            catch (ArgumentOutOfRangeException exception)
            {
                Log.Warn(LogTag, "Install status timestamp invalid: " + exception.GetType().Name);
                return null;
            }
        }
    }

    /// <summary>Удаляет persisted-статус (например, после live-отображения в открытом UI).</summary>
    public void Clear()
    {
        lock (_sync)
        {
            var editor = _preferences?.Edit();
            editor?.Remove(LastStatusKey);
            editor?.Remove(LastStatusAtKey);
            editor?.Apply();
        }
    }
}

/// <summary>Persisted-снимок статуса установки с временем записи (UTC).</summary>
/// <param name="Status">Статус установки.</param>
/// <param name="RecordedAt">Момент записи статуса.</param>
public readonly record struct UpdateInstallStatusSnapshot(UpdateInstallStatus Status, DateTimeOffset RecordedAt);
