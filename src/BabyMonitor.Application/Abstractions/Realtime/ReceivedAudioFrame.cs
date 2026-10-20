namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Принятый закодированный аудиокадр (Opus payload). Кодек и его профиль — ответственность
/// transport-адаптера; Domain и Application не интерпретируют байты аудио.
/// </summary>
/// <remarks>
/// Владение буфером: <see cref="Payload"/> действителен только до следующего шага перечисления
/// <see cref="IRealtimeTransportSession.ReceiveAudioAsync"/>. Адаптер обязан не переиспользовать
/// буфер до возврата управления потребителю; потребитель копирует данные, если они нужны дольше.
/// <para>
/// <c>default(ReceivedAudioFrame)</c> не является валидным кадром (пустой payload) и не должен
/// передаваться между слоями: адаптеры обязаны создавать кадры через конструктор. Отсутствие
/// кадра моделируется отсутствием элемента в потоке, а не default-значением.
/// </para>
/// </remarks>
public readonly struct ReceivedAudioFrame
{
    /// <summary>Создаёт ссылку на принятый кадр.</summary>
    /// <param name="payload">Закодированный Opus-кадр непустой длины.</param>
    /// <exception cref="ArgumentException">Кадр пуст.</exception>
    public ReceivedAudioFrame(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            throw new ArgumentException("Принятый аудиокадр не может быть пустым.", nameof(payload));
        }

        Payload = payload;
    }

    /// <summary>Закодированный аудиокадр; валиден в рамках шага перечисления.</summary>
    public ReadOnlyMemory<byte> Payload { get; }
}
