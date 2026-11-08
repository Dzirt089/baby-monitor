namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Порт кодека Opus (encode PCM → Opus, decode Opus → PCM) для audio-only pipeline. Реализация —
/// Infrastructure (SIPSorcery/Concentus или MediaCodec), Application не видит библиотек.
/// Экземпляр не потокобезопасен: один экземпляр на сессию/направление, вызовы сериализуются
/// владельцем. Аудио не логируется и не сохраняется.
/// </summary>
public interface IAudioCodec
{
    /// <summary>Максимальный размер одного Opus-кадра (bytes) для 20 мс mono 48 kHz.</summary>
    int MaxEncodedBytes => 1275;

    /// <summary>PCM-формат входа/выхода.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Кодирует один PCM-блок (ровно 20 мс: <see cref="AudioFormat.Frame20MsBytes"/> байт) в Opus.
    /// </summary>
    /// <param name="pcm">PCM-блок 20 мс.</param>
    /// <param name="encodedDestination">Буфер назначения не меньше <see cref="MaxEncodedBytes"/>.</param>
    /// <returns>Количество записанных байт Opus-кадра (&gt; 0).</returns>
    /// <exception cref="ArgumentException">Неверный размер PCM-блока или буфера.</exception>
    int Encode(ReadOnlySpan<byte> pcm, Span<byte> encodedDestination);

    /// <summary>
    /// Декодирует один Opus-кадр в PCM 20 мс.
    /// </summary>
    /// <param name="encoded">Opus-кадр.</param>
    /// <param name="pcmDestination">Буфер назначения не меньше <see cref="AudioFormat.Frame20MsBytes"/>.</param>
    /// <returns>Количество записанных PCM-байт.</returns>
    /// <exception cref="ArgumentException">Неверный размер буфера.</exception>
    int Decode(ReadOnlySpan<byte> encoded, Span<byte> pcmDestination);
}
