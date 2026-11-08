namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>Параметры запуска воспроизведения (Parent). Значения — измеряемые настройки, не скрытые.</summary>
public sealed record AudioPlaybackOptions
{
    /// <summary>Создаёт параметры воспроизведения.</summary>
    /// <param name="format">PCM-формат; null — <see cref="AudioFormat.Default"/>.</param>
    /// <param name="preferredBufferMilliseconds">Желаемая длительность буфера воспроизведения.</param>
    /// <exception cref="ArgumentOutOfRangeException">Буфер вне допустимого диапазона.</exception>
    public AudioPlaybackOptions(AudioFormat? format = null, int preferredBufferMilliseconds = 100)
    {
        if (preferredBufferMilliseconds is < 20 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredBufferMilliseconds),
                preferredBufferMilliseconds,
                "Буфер воспроизведения должен быть в диапазоне 20..500 мс.");
        }

        Format = format ?? AudioFormat.Default;
        PreferredBufferMilliseconds = preferredBufferMilliseconds;
    }

    /// <summary>PCM-формат.</summary>
    public AudioFormat Format { get; }

    /// <summary>Желаемая длительность буфера воспроизведения, мс.</summary>
    public int PreferredBufferMilliseconds { get; }
}
