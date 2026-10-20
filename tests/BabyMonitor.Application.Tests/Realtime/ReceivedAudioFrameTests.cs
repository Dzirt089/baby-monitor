using BabyMonitor.Application.Abstractions.Realtime;

namespace BabyMonitor.Application.Tests.Realtime;

/// <summary>
/// Контракт принятого аудиокадра: непустой payload, сохранение содержимого и фактическая
/// семантика <c>default(ReceivedAudioFrame)</c>. Кадр ссылается на буфер адаптера и валиден
/// только в рамках шага перечисления (потребитель копирует данные, если нужны дольше).
/// </summary>
public sealed class ReceivedAudioFrameTests
{
    [Fact]
    public void Создание_кадра_с_пустым_ReadOnlyMemory_бросает_ArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ReceivedAudioFrame(ReadOnlyMemory<byte>.Empty));

        Assert.Equal("payload", exception.ParamName);
    }

    [Fact]
    public void Создание_кадра_с_пустым_массивом_бросает_ArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ReceivedAudioFrame(Array.Empty<byte>()));

        Assert.Equal("payload", exception.ParamName);
    }

    [Fact]
    public void Непустой_payload_сохраняется()
    {
        byte[] opusFrame = [0x4F, 0x70, 0x75, 0x73, 0x00];

        var frame = new ReceivedAudioFrame(opusFrame);

        Assert.Equal(opusFrame.Length, frame.Payload.Length);
        Assert.Equal(opusFrame, frame.Payload.ToArray());
    }

    [Fact]
    public void Payload_из_одного_нулевого_байта_считается_непустым()
    {
        var frame = new ReceivedAudioFrame(new byte[] { 0 });

        Assert.Equal(1, frame.Payload.Length);
    }

    [Fact]
    public void Default_структура_содержит_пустой_payload_вопреки_guard_конструктора()
    {
        ReceivedAudioFrame frame = default;

        // readonly struct допускает default, который не проходит через конструктор.
        // Контракт (ReceivedAudioFrame XML) явно описывает default как НЕВАЛИДНЫЙ кадр:
        // отсутствие кадра моделируется отсутствием элемента в потоке, а не default-значением.
        // Characterization-тест фиксирует это поведение; struct выбран, чтобы не аллоцировать
        // объект на каждый аудиокадр в real-time пути.
        Assert.True(frame.Payload.IsEmpty);
        Assert.Equal(0, frame.Payload.Length);
    }
}
