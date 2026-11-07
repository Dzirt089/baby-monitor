namespace BabyMonitor.Application.Monitoring;

/// <summary>
/// Политика автоматического переподключения: exponential backoff с детерминированным jitter
/// и верхним пределом. Один объект конфигурации на приложение; TimeProvider не нужен, задержка
/// вычисляется из номера попытки (jitter также детерминирован), sleep выполняет вызывающий слой.
/// </summary>
public sealed class ReconnectPolicy
{
    /// <summary>Создаёт политику.</summary>
    /// <param name="maxAttempts">Максимум попыток, 1..100.</param>
    /// <param name="initialDelay">Задержка первой попытки.</param>
    /// <param name="maxDelay">Верхний предел задержки.</param>
    /// <param name="factor">Множитель exponential backoff, 1..4.</param>
    /// <param name="jitterRatio">Доля jitter, 0..1 (детерминированная).</param>
    /// <exception cref="ArgumentOutOfRangeException">Некорректные параметры.</exception>
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

        if (factor is < 1.0 or > 4.0)
        {
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Множитель: 1..4.");
        }

        if (jitterRatio is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(jitterRatio), jitterRatio, "Jitter ratio: 0..1.");
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

    /// <summary>Верхний предел задержки.</summary>
    public TimeSpan MaxDelay { get; }

    /// <summary>Множитель backoff.</summary>
    public double Factor { get; }

    /// <summary>Доля jitter.</summary>
    public double JitterRatio { get; }

    /// <summary>Вычисляет задержку для попытки (1-based).</summary>
    /// <param name="attempt">Номер попытки, начиная с 1.</param>
    /// <returns>Задержка с детерминированным jitter в пределах [base*(1-jitter), base*(1+jitter)].</returns>
    /// <exception cref="ArgumentOutOfRangeException">attempt &lt; 1.</exception>
    public TimeSpan GetDelay(int attempt)
    {
        if (attempt < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempt), attempt, "Номер попытки начинается с 1.");
        }

        var exponential = InitialDelay.TotalMilliseconds * Math.Pow(Factor, attempt - 1);
        var capped = Math.Min(exponential, MaxDelay.TotalMilliseconds);
        var unit = DeterministicUnit(attempt);
        var jitter = 1.0 + ((unit * 2.0) - 1.0) * JitterRatio;
        var milliseconds = Math.Max(1.0, capped * jitter);
        return TimeSpan.FromMilliseconds(milliseconds);
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
