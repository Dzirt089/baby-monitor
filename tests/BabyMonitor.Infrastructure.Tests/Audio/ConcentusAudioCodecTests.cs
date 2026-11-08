using System.Buffers.Binary;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Infrastructure.Audio;

namespace BabyMonitor.Infrastructure.Tests.Audio;

/// <summary>
/// Contract-тесты Opus-кодека: round-trip 20 мс кадров 48 kHz mono 16-bit, лимиты буферов,
/// MaxEncodedBytes и обработка некорректных данных.
/// </summary>
public sealed class ConcentusAudioCodecTests
{
    private const int FrameSamples = 960;
    private const int FrameBytes = FrameSamples * sizeof(short);

    [Fact]
    public void Format_и_MaxEncodedBytes_соответствуют_контракту()
    {
        using var codec = new ConcentusAudioCodec();

        Assert.Equal(AudioFormat.Default, codec.Format);
        Assert.Equal(48_000, codec.Format.SampleRateHz);
        Assert.Equal(1, codec.Format.Channels);
        Assert.Equal(16, codec.Format.BitsPerSample);
        Assert.Equal(1920, codec.Format.Frame20MsBytes);
        Assert.Equal(1275, codec.MaxEncodedBytes);
    }

    [Fact]
    public void Roundtrip_нескольких_20мс_кадров_даёт_PCM_того_же_размера()
    {
        using var codec = new ConcentusAudioCodec();
        var encoded = new byte[codec.MaxEncodedBytes];
        var decoded = new byte[codec.Format.Frame20MsBytes];
        var pcm = CreateSineFrame(440);

        for (var frameIndex = 0; frameIndex < 5; frameIndex++)
        {
            var encodedBytes = codec.Encode(pcm, encoded);
            Assert.InRange(encodedBytes, 1, codec.MaxEncodedBytes);

            var decodedBytes = codec.Decode(encoded.AsSpan(0, encodedBytes), decoded);
            Assert.Equal(FrameBytes, decodedBytes);
            Assert.True(HasSignal(decoded), "Декодированный PCM не должен быть тишиной для синусоиды.");
        }
    }

    [Fact]
    public void Roundtrip_между_разными_экземплярами_кодека()
    {
        using var encoderCodec = new ConcentusAudioCodec();
        using var decoderCodec = new ConcentusAudioCodec();
        var pcm = CreateSineFrame(1000);
        var encoded = new byte[encoderCodec.MaxEncodedBytes];
        var encodedBytes = encoderCodec.Encode(pcm, encoded);
        var decoded = new byte[decoderCodec.Format.Frame20MsBytes];

        var decodedBytes = decoderCodec.Decode(encoded.AsSpan(0, encodedBytes), decoded);

        Assert.Equal(FrameBytes, decodedBytes);
        Assert.True(HasSignal(decoded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1919)]
    [InlineData(1921)]
    public void Encode_с_неверным_размером_PCM_бросает_ArgumentException(int pcmSize)
    {
        using var codec = new ConcentusAudioCodec();
        var pcm = new byte[pcmSize];
        var encoded = new byte[codec.MaxEncodedBytes];

        Assert.Throws<ArgumentException>(() => codec.Encode(pcm, encoded));
    }

    [Fact]
    public void Encode_с_маленьким_буфером_назначения_бросает_ArgumentException()
    {
        using var codec = new ConcentusAudioCodec();
        var pcm = new byte[FrameBytes];
        var destination = new byte[codec.MaxEncodedBytes - 1];

        Assert.Throws<ArgumentException>(() => codec.Encode(pcm, destination));
    }

    [Fact]
    public void Decode_с_маленьким_буфером_назначения_бросает_ArgumentException()
    {
        using var codec = new ConcentusAudioCodec();
        var encoded = new byte[codec.MaxEncodedBytes];
        var encodedBytes = codec.Encode(new byte[FrameBytes], encoded);
        var destination = new byte[FrameBytes - 1];

        Assert.Throws<ArgumentException>(() => codec.Decode(encoded.AsSpan(0, encodedBytes), destination));
    }

    [Fact]
    public void Decode_пустого_кадра_бросает_ArgumentException()
    {
        using var codec = new ConcentusAudioCodec();
        var destination = new byte[FrameBytes];

        Assert.Throws<ArgumentException>(() => codec.Decode(ReadOnlySpan<byte>.Empty, destination));
    }

    [Fact]
    public void Decode_повреждённого_кадра_бросает_ArgumentException()
    {
        using var codec = new ConcentusAudioCodec();
        var destination = new byte[FrameBytes];
        var corrupted = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        Assert.Throws<ArgumentException>(() => codec.Decode(corrupted, destination));
    }

    [Fact]
    public void После_Dispose_кодек_бросает_ObjectDisposedException()
    {
        var codec = new ConcentusAudioCodec();
        codec.Dispose();

        Assert.Throws<ObjectDisposedException>(() => codec.Encode(new byte[FrameBytes], new byte[codec.MaxEncodedBytes]));
        Assert.Throws<ObjectDisposedException>(() => codec.Decode(new byte[] { 1, 2, 3 }, new byte[FrameBytes]));
    }

    [Fact]
    public void Некорректный_профиль_Opus_отклоняется()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConcentusAudioCodec(new OpusCodecOptions { Bitrate = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConcentusAudioCodec(new OpusCodecOptions { Complexity = 11 }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ConcentusAudioCodec(new OpusCodecOptions { PacketLossPercent = 101 }));
    }

    private static byte[] CreateSineFrame(int frequencyHz)
    {
        var pcm = new byte[FrameBytes];
        for (var sampleIndex = 0; sampleIndex < FrameSamples; sampleIndex++)
        {
            var value = (short)(Math.Sin(2 * Math.PI * frequencyHz * sampleIndex / 48_000) * 8_000);
            BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(sampleIndex * sizeof(short)), value);
        }

        return pcm;
    }

    private static bool HasSignal(byte[] pcm)
    {
        for (var index = 0; index < pcm.Length; index += sizeof(short))
        {
            if (BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(index)) != 0)
            {
                return true;
            }
        }

        return false;
    }
}
