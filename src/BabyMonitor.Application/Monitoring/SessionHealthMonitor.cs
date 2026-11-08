using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Application.Monitoring;

/// <summary>
/// Вычисляет SessionHealth по heartbeat/media timestamps. Control и media проверяются независимо:
/// heartbeat missed и отсутствие media flow — разные состояния. TimeProvider инжектируется.
/// </summary>
public sealed class SessionHealthMonitor
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _heartbeatTimeout;
    private readonly TimeSpan _mediaTimeout;
    private DateTimeOffset? _lastHeartbeat;
    private DateTimeOffset? _lastMediaFrame;

    /// <summary>Создаёт монитор здоровья.</summary>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <param name="heartbeatTimeout">Порог потери heartbeat.</param>
    /// <param name="mediaTimeout">Порог остановки media flow.</param>
    /// <exception cref="ArgumentOutOfRangeException">Некорректные пороги.</exception>
    public SessionHealthMonitor(TimeProvider timeProvider, TimeSpan? heartbeatTimeout = null, TimeSpan? mediaTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _heartbeatTimeout = heartbeatTimeout ?? TimeSpan.FromSeconds(10);
        _mediaTimeout = mediaTimeout ?? TimeSpan.FromSeconds(2);
        if (_heartbeatTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(heartbeatTimeout), _heartbeatTimeout, "Порог heartbeat должен быть больше нуля.");
        }

        if (_mediaTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(mediaTimeout), _mediaTimeout, "Порог media должен быть больше нуля.");
        }
    }

    /// <summary>Порог остановки media flow (например, ожидания первого аудиокадра).</summary>
    public TimeSpan MediaTimeout => _mediaTimeout;

    /// <summary>Отмечает полученный control heartbeat.</summary>
    public void MarkHeartbeat()
    {
        lock (_sync)
        {
            _lastHeartbeat = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Отмечает полученный media frame.</summary>
    public void MarkMediaFrame()
    {
        lock (_sync)
        {
            _lastMediaFrame = _timeProvider.GetUtcNow();
        }
    }

    /// <summary>Сбрасывает измерения (например, перед reconnect).</summary>
    public void Reset()
    {
        lock (_sync)
        {
            _lastHeartbeat = null;
            _lastMediaFrame = null;
        }
    }

    /// <summary>Вычисляет health-снимок для роли.</summary>
    /// <param name="localRole">Роль локального устройства (Child — capture, Parent — playback).</param>
    /// <returns>Immutable health-снимок.</returns>
    public SessionHealth Evaluate(DeviceRole localRole)
    {
        if (localRole is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Health требует роль Child или Parent.", nameof(localRole));
        }

        lock (_sync)
        {
            var now = _timeProvider.GetUtcNow();
            var control = EvaluateControl(now);
            var media = EvaluateMedia(now);
            return new SessionHealth(
                control,
                media,
                captureActive: localRole is DeviceRole.Child && media is AudioFlowState.Flowing,
                playbackActive: localRole is DeviceRole.Parent && media is AudioFlowState.Flowing);
        }
    }

    private SessionChannelState EvaluateControl(DateTimeOffset now)
    {
        if (_lastHeartbeat is null)
        {
            return SessionChannelState.Unknown;
        }

        var elapsed = now - _lastHeartbeat.Value;
        if (elapsed <= _heartbeatTimeout / 2)
        {
            return SessionChannelState.Connected;
        }

        return elapsed <= _heartbeatTimeout ? SessionChannelState.Degraded : SessionChannelState.Lost;
    }

    private AudioFlowState EvaluateMedia(DateTimeOffset now)
    {
        if (_lastMediaFrame is null)
        {
            return AudioFlowState.Inactive;
        }

        return now - _lastMediaFrame.Value <= _mediaTimeout ? AudioFlowState.Flowing : AudioFlowState.Stalled;
    }
}
