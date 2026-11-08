namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>Параметры запуска захвата микрофона (Child). Значения — измеряемые настройки, не скрытые.</summary>
public sealed record AudioCaptureOptions
{
    /// <summary>Создаёт параметры захвата.</summary>
    /// <param name="format">PCM-формат; null — <see cref="AudioFormat.Default"/>.</param>
    /// <param name="preferredBufferMilliseconds">Желаемая длительность буфера захвата.</param>
    /// <exception cref="ArgumentOutOfRangeException">Буфер вне допустимого диапазона.</exception>
    public AudioCaptureOptions(AudioFormat? format = null, int preferredBufferMilliseconds = 100)
    {
        if (preferredBufferMilliseconds is < 20 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(preferredBufferMilliseconds),
                preferredBufferMilliseconds,
                "Буфер захвата должен быть в диапазоне 20..500 мс.");
        }

        Format = format ?? AudioFormat.Default;
        PreferredBufferMilliseconds = preferredBufferMilliseconds;
    }

    /// <summary>PCM-формат.</summary>
    public AudioFormat Format { get; }

    /// <summary>Желаемая длительность буфера захвата, мс.</summary>
    public int PreferredBufferMilliseconds { get; }
}
