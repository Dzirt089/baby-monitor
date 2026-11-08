using System.Runtime.InteropServices;
using BabyMonitor.Application.Abstractions.Audio;
using Concentus;
using Concentus.Enums;

namespace BabyMonitor.Infrastructure.Audio;

/// <summary>
/// Стартовый профиль Opus-кодека (измеряемые настройки, пересматриваются по device-метрикам):
/// 32 kbps VBR, in-band FEC и оценка потерь 10% для нестабильного Wi-Fi. Профиль общий для
/// encode/decode; формат PCM фиксирован контрактом <see cref="IAudioCodec"/>.
/// </summary>
public sealed record OpusCodecOptions
{
    /// <summary>Профиль по умолчанию для audio-only MVP.</summary>
    public static OpusCodecOptions Default { get; } = new();

    /// <summary>Целевой битрейт Opus, бит/с (6000..510000).</summary>
    public int Bitrate { get; init; } = 32_000;

    /// <summary>Сложность кодера 0..10 (баланс CPU/качество).</summary>
    public int Complexity { get; init; } = 5;

    /// <summary>In-band FEC для устойчивости к потерям RTP.</summary>
    public bool UseInbandFec { get; init; } = true;

    /// <summary>Ожидаемый процент потерь для настройки FEC (0..100).</summary>
    public int PacketLossPercent { get; init; } = 10;
}

/// <summary>
/// Opus-кодек 48 kHz mono 16-bit PCM, кадр 20 мс (960 сэмплов, 1920 байт PCM), реализация порта
/// <see cref="IAudioCodec"/> на Concentus (транзитивная зависимость SIPSorcery, ADR-0005).
/// Аудио не логируется и не сохраняется; экземпляр не потокобезопасен (один на направление,
/// вызовы сериализуются владельцем). PCM — little-endian 16-bit, как отдаёт Android AudioRecord.
/// </summary>
public sealed class ConcentusAudioCodec : IAudioCodec, IDisposable
{
    private const int FrameSamples = 960;
    private const int FrameBytes = FrameSamples * sizeof(short);
    private const int MaxEncodedBytesValue = 1275;
    private const int MinBitrate = 6_000;
    private const int MaxBitrate = 510_000;
    private const int MinComplexity = 0;
    private const int MaxComplexity = 10;
    private const int MinPacketLossPercent = 0;
    private const int MaxPacketLossPercent = 100;

    private readonly IOpusEncoder _encoder;
    private readonly IOpusDecoder _decoder;
    private bool _disposed;

    /// <summary>Создаёт Opus-кодек с заданным профилем (по умолчанию <see cref="OpusCodecOptions.Default"/>).</summary>
    /// <param name="options">Профиль кодека; <see langword="null"/> — профиль по умолчанию.</param>
    /// <exception cref="ArgumentOutOfRangeException">Параметры профиля вне допустимого диапазона.</exception>
    public ConcentusAudioCodec(OpusCodecOptions? options = null)
    {
        var settings = options ?? OpusCodecOptions.Default;
        ValidateSettings(settings);

        _encoder = OpusCodecFactory.CreateEncoder(
            Format.SampleRateHz,
            Format.Channels,
            OpusApplication.OPUS_APPLICATION_AUDIO);
        _encoder.Bitrate = settings.Bitrate;
        _encoder.Complexity = settings.Complexity;
        _encoder.UseInbandFEC = settings.UseInbandFec;
        _encoder.PacketLossPercent = settings.PacketLossPercent;
        _decoder = OpusCodecFactory.CreateDecoder(Format.SampleRateHz, Format.Channels);
    }

    /// <inheritdoc />
    public AudioFormat Format => AudioFormat.Default;

    /// <inheritdoc />
    public int MaxEncodedBytes => MaxEncodedBytesValue;

    /// <inheritdoc />
    public int Encode(ReadOnlySpan<byte> pcm, Span<byte> encodedDestination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (pcm.Length != Format.Frame20MsBytes)
        {
            throw new ArgumentException(
                "PCM-блок должен содержать ровно 20 мс (1920 байт для 48 kHz mono 16-bit).",
                nameof(pcm));
        }

        if (encodedDestination.Length < MaxEncodedBytesValue)
        {
            throw new ArgumentException(
                "Буфер назначения Opus меньше MaxEncodedBytes (1275 байт).",
                nameof(encodedDestination));
        }

        var samples = MemoryMarshal.Cast<byte, short>(pcm);
        try
        {
            var written = _encoder.Encode(samples, FrameSamples, encodedDestination, MaxEncodedBytesValue);
            if (written <= 0)
            {
                throw new ArgumentException("Не удалось закодировать PCM-блок в Opus.", nameof(pcm));
            }

            return written;
        }
        catch (OpusException exception)
        {
            throw new ArgumentException("Не удалось закодировать PCM-блок в Opus.", nameof(pcm), exception);
        }
    }

    /// <inheritdoc />
    public int Decode(ReadOnlySpan<byte> encoded, Span<byte> pcmDestination)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (encoded.IsEmpty)
        {
            throw new ArgumentException("Opus-кадр не может быть пустым.", nameof(encoded));
        }

        if (pcmDestination.Length < FrameBytes)
        {
            throw new ArgumentException(
                "Буфер назначения PCM меньше одного кадра 20 мс (1920 байт).",
                nameof(pcmDestination));
        }

        var samples = MemoryMarshal.Cast<byte, short>(pcmDestination);
        int decodedSamples;
        try
        {
            decodedSamples = _decoder.Decode(encoded, samples, FrameSamples);
        }
        catch (OpusException exception)
        {
            throw new ArgumentException("Opus-кадр повреждён и не декодируется.", nameof(encoded), exception);
        }

        if (decodedSamples <= 0)
        {
            throw new ArgumentException("Opus-кадр повреждён и не декодируется.", nameof(encoded));
        }

        return decodedSamples * Format.Channels * (Format.BitsPerSample / 8);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_encoder is IDisposable encoderDisposable)
        {
            encoderDisposable.Dispose();
        }

        if (_decoder is IDisposable decoderDisposable)
        {
            decoderDisposable.Dispose();
        }
    }

    private static void ValidateSettings(OpusCodecOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.Bitrate is < MinBitrate or > MaxBitrate)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Bitrate, "Битрейт Opus вне диапазона 6000..510000.");
        }

        if (settings.Complexity is < MinComplexity or > MaxComplexity)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.Complexity, "Сложность Opus вне диапазона 0..10.");
        }

        if (settings.PacketLossPercent is < MinPacketLossPercent or > MaxPacketLossPercent)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), settings.PacketLossPercent, "Оценка потерь вне диапазона 0..100.");
        }
    }
}
