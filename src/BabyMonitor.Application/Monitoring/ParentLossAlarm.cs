namespace BabyMonitor.Application.Monitoring;

/// <summary>
/// Локальная тревога Parent при потере peer: поднимается по локальному таймеру, не ожидая ответа
/// Child, и не зависит от состояния Child-устройства. TimeProvider инжектируется.
/// </summary>
public sealed class ParentLossAlarm
{
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _threshold;
    private DateTimeOffset? _lastAlive;

    /// <summary>Создаёт тревогу.</summary>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <param name="threshold">Порог потери связи (по умолчанию 15 секунд).</param>
    /// <exception cref="ArgumentOutOfRangeException">Порог не положителен.</exception>
    public ParentLossAlarm(TimeProvider timeProvider, TimeSpan? threshold = null)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
        _threshold = threshold ?? TimeSpan.FromSeconds(15);
        if (_threshold <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), _threshold, "Порог тревоги должен быть больше нуля.");
        }
    }

    /// <summary>Поднята ли тревога.</summary>
    public bool IsRaised { get; private set; }

    /// <summary>Событие изменения состояния тревоги; sender — аргумент <see cref="bool"/> (raised).</summary>
    public event EventHandler<bool>? AlarmChanged;

    /// <summary>Отмечает подтверждённую жизнь peer (heartbeat/media).</summary>
    public void ReportPeerAlive() => _lastAlive = _timeProvider.GetUtcNow();

    /// <summary>
    /// Оценивает тревогу по локальному времени. Если жизнь peer ещё ни разу не подтверждалась,
    /// тревога не поднимается (защита от ложного срабатывания до старта media).
    /// </summary>
    /// <returns>true, если тревога активна.</returns>
    public bool Evaluate()
    {
        var now = _timeProvider.GetUtcNow();
        var shouldRaise = _lastAlive is not null && now - _lastAlive.Value >= _threshold;
        if (shouldRaise == IsRaised)
        {
            return IsRaised;
        }

        IsRaised = shouldRaise;
        AlarmChanged?.Invoke(this, IsRaised);
        return IsRaised;
    }

    /// <summary>Сбрасывает тревогу и историю жизни peer.</summary>
    public void Reset()
    {
        _lastAlive = null;
        if (IsRaised)
        {
            IsRaised = false;
            AlarmChanged?.Invoke(this, false);
        }
    }
}
