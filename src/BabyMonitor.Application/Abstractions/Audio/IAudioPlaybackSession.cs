namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Активная сессия воспроизведения (Parent). Принимает 16-bit PCM из предоставленного вызывающим
/// буфера; реализация — Platform.Android (AudioTrack + audio focus/route). Порт не содержит
/// Android-типов.
/// </summary>
public interface IAudioPlaybackSession : IAsyncDisposable
{
    /// <summary>Формат сессии.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Записывает PCM-блок в воспроизведение. Не блокирует UI-поток; очередь ограничена, при
    /// переполнении допускается отбрасывание старых данных (latency не растёт безгранично).
    /// </summary>
    /// <param name="pcm">PCM-данные (кратно кадру 20 мс).</param>
    /// <param name="cancellationToken">Отмена записи/остановки сессии.</param>
    /// <exception cref="OperationCanceledException">Запись отменена.</exception>
    ValueTask WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken);

    /// <summary>Останавливает воспроизведение; повторный вызов идемпотентен.</summary>
    /// <param name="cancellationToken">Ограничение ожидания остановки.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
