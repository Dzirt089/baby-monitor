namespace BabyMonitor.Application.Monitoring;

/// <summary>
/// Политика автоматического переподключения: exponential backoff с детерминированным jitter
/// и верхним пределом (final delay никогда не превышает MaxDelay). Один объект конфигурации;
/// sleep выполняет вызывающий слой с TimeProvider.
/// </summary>
public sealed class ReconnectPolicy
{
    /// <summary>Создаёт политику.</summary>
    /// <param name="maxAttempts">Максимум попыток, 1..100.</param>
    /// <param name="initialDelay">Задержка первой попытки.</param>
    /// <param name="maxDelay">Верхний предел задержки (final cap после jitter).</param>
    /// <param name="factor">Множитель exponential backoff, 1..4.</param>
    /// <param name="jitterRatio">Доля jitter, 0..1 (детерминированная).</param>
    /// <exception cref="ArgumentOutOfRangeException">Некорректные/нечисловые параметры.</exception>
    public ReconnectPolicy(
        int maxAttempts = 5,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        double factor = 2.0,
        double jitterRatio = 0.2)
    {
        if (maxAttempts is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), maxAttempts, "Максимум попыток: 1..100.");
        }

        var initial = initialDelay ?? TimeSpan.FromMilliseconds(500);
        var max = maxDelay ?? TimeSpan.FromSeconds(30);
        if (initial <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(initialDelay), initial, "Начальная задержка должна быть больше нуля.");
        }

        if (max < initial)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDelay), max, "Максимальная задержка меньше начальной.");
        }

        if (!double.IsFinite(factor) || factor is < 1.0 or > 4.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Множитель: конечное число 1..4.");
        }

        if (!double.IsFinite(jitterRatio) || jitterRatio is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterRatio), jitterRatio, "Jitter ratio: конечное число 0..1.");
        }

        MaxAttempts = maxAttempts;
        InitialDelay = initial;
        MaxDelay = max;
        Factor = factor;
        JitterRatio = jitterRatio;
    }

    /// <summary>Максимум попыток.</summary>
    public int MaxAttempts { get; }

    /// <summary>Задержка первой попытки.</summary>
    public TimeSpan InitialDelay { get; }

    /// <summary>Верхний предел финальной задержки (после jitter).</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Множитель backoff.</summary>
    public double Factor { get; }

    /// <summary>Доля jitter.</summary>
    public double JitterRatio { get; }

    /// <summary>Можно ли выполнять попытку с указанным номером.</summary>
    /// <param name="attempt">Номер попытки (1-based).</param>
    /// <returns>true, если 1 &lt;= attempt &lt;= MaxAttempts.</returns>
    public bool CanAttempt(int attempt) => attempt >= 1 && attempt <= MaxAttempts;

    /// <summary>Вычисляет задержку для попытки (1-based).</summary>
    /// <param name="attempt">Номер попытки, 1..MaxAttempts.</param>
    /// <returns>Задержка с детерминированным jitter, не превышающая MaxDelay.</returns>
    /// <exception cref="ArgumentOutOfRangeException">attempt вне 1..MaxAttempts.</exception>
    public TimeSpan GetDelay(int attempt)
    {
        if (!CanAttempt(attempt))
        {
            throw new ArgumentOutOfRangeException(
                nameof(attempt),
                attempt,
                $"Номер попытки должен быть в диапазоне 1..{MaxAttempts}.");
        }

        var exponential = InitialDelay.TotalMilliseconds * Math.Pow(Factor, attempt - 1);
        var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        var unit = DeterministicUnit(attempt);
        var jitter = 1.0 + ((unit * 2.0) - 1.0) * JitterRatio;
        var milliseconds = Math.Min(capped * jitter, MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(Math.Max(1.0, milliseconds));
    }

    private static double DeterministicUnit(int attempt)
    {
        unchecked
        {
            var hash = (uint)(attempt * 2654435761);
            hash ^= hash >> 16;
            return (hash % 10_000u) / 10_000.0;
        }
    }
}
