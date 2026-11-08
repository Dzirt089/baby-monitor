namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Формат PCM-аудио, с которым обмениваются Application-порты. Для MVP поддержан только
/// моно 48 кГц 16 бит (профиль Opus 20 мс); расширение требует отдельного решения и тестов.
/// </summary>
public sealed record AudioFormat
{
    /// <summary>Формат по умолчанию: mono 48 kHz 16 bit PCM.</summary>
    public static AudioFormat Default { get; } = new(48_000, 1, 16);

    /// <summary>Создаёт формат и проверяет допустимый профиль.</summary>
    /// <param name="sampleRateHz">Частота дискретизации, Гц.</param>
    /// <param name="channels">Количество каналов (1 для MVP).</param>
    /// <param name="bitsPerSample">Разрядность сэмпла (16 для MVP).</param>
    /// <exception cref="ArgumentException">Неподдерживаемый формат.</exception>
    public AudioFormat(int sampleRateHz, int channels, int bitsPerSample)
    {
        if (sampleRateHz != 48_000)
        {
            throw new ArgumentException("Поддерживается только 48 kHz (Opus-профиль).", nameof(sampleRateHz));
        }

        if (channels != 1)
        {
            throw new ArgumentException("Поддерживается только mono (1 канал).", nameof(channels));
        }

        if (bitsPerSample != 16)
        {
            throw new ArgumentException("Поддерживается только 16-bit PCM.", nameof(bitsPerSample));
        }

        SampleRateHz = sampleRateHz;
        Channels = channels;
        BitsPerSample = bitsPerSample;
    }

    /// <summary>Частота дискретизации, Гц.</summary>
    public int SampleRateHz { get; }

    /// <summary>Количество каналов.</summary>
    public int Channels { get; }

    /// <summary>Разрядность сэмпла (бит).</summary>
    public int BitsPerSample { get; }

    /// <summary>Размер одного кадра 20 мс в байтах.</summary>
    public int Frame20MsBytes => SampleRateHz / 50 * Channels * (BitsPerSample / 8);
}
