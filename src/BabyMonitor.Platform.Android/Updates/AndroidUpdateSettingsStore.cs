using Android.Content;
using Android.Util;
using BabyMonitor.Application.Updates;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// SharedPreferences-хранилище настроек автообновления: флаг автопроверки (по умолчанию включена)
/// и время последней успешной проверки в UTC Unix ms (суточный throttle AppUpdateCoordinator).
/// Потокобезопасно; секретов/PII не хранит.
/// </summary>
public sealed class AndroidUpdateSettingsStore : IUpdateSettingsStore
{
    private const string PreferencesName = "babymonitor_updates";
    private const string AutoCheckEnabledKey = "auto_check_enabled";
    private const string LastSuccessfulCheckAtKey = "last_successful_check_at_unix_ms";
    private const long NoTimestamp = -1;
    private const string LogTag = "BabyMonitor.Updates";

    private readonly object _sync = new();
    private readonly ISharedPreferences? _preferences;

    /// <summary>Создаёт хранилище настроек обновлений.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public AndroidUpdateSettingsStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext ?? context;
        _preferences = applicationContext.GetSharedPreferences(PreferencesName, FileCreationMode.Private);
    }

    /// <inheritdoc />
    public bool AutoCheckEnabled
    {
        get
        {
            lock (_sync)
            {
                return _preferences?.GetBoolean(AutoCheckEnabledKey, true) ?? true;
            }
        }
        set
        {
            lock (_sync)
            {
                _preferences?.Edit()?.PutBoolean(AutoCheckEnabledKey, value)?.Apply();
            }
        }
    }

    /// <inheritdoc />
    public DateTimeOffset? LastSuccessfulCheckAt
    {
        get
        {
            long unixMilliseconds;
            lock (_sync)
            {
                unixMilliseconds = _preferences?.GetLong(LastSuccessfulCheckAtKey, NoTimestamp) ?? NoTimestamp;
            }

            if (unixMilliseconds <= 0)
            {
                return null;
            }

            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(unixMilliseconds);
            }
            catch (ArgumentOutOfRangeException exception)
            {
                // Повреждённое persisted-значение не должно ломать throttle: считаем, что проверок не было.
                Log.Warn(LogTag, "Update settings timestamp invalid: " + exception.GetType().Name);
                return null;
            }
        }
        set
        {
            lock (_sync)
            {
                var editor = _preferences?.Edit();
                if (editor is null)
                {
                    return;
                }

                if (value is null)
                {
                    editor.Remove(LastSuccessfulCheckAtKey);
                }
                else
                {
                    editor.PutLong(LastSuccessfulCheckAtKey, value.Value.ToUnixTimeMilliseconds());
                }

                editor.Apply();
            }
        }
    }
}
