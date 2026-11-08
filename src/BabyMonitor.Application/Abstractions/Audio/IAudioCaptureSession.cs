namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Активная сессия захвата микрофона (Child). Читает 16-bit PCM в предоставленный вызывающим
/// буфер: Application сам управляет памятью и не получает pooled-буферы адаптера. Порт не содержит
/// Android-типов; реализация — Platform.Android.
/// </summary>
public interface IAudioCaptureSession : IAsyncDisposable
{
    /// <summary>Формат сессии.</summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Читает очередной блок PCM-данных в буфер вызывающего. Возвращает число записанных байт
    /// (кратно размеру кадра 20 мс); во время работы ожидание асинхронное, 0 возвращается только
    /// после StopAsync (конец потока). Вызов не должен блокировать UI-поток.
    /// Runtime-отказ устройства может подниматься как <see cref="System.IO.IOException"/>; вызывающий
    /// слой обязан преобразовать его в типизированную ошибку (<see cref="AudioErrorCodes"/>).
    /// </summary>
    /// <param name="destination">Буфер вызывающего (не меньше одного кадра 20 мс).</param>
    /// <param name="cancellationToken">Отмена чтения/остановки сессии.</param>
    /// <returns>Количество записанных байт.</returns>
    /// <exception cref="ArgumentException">Буфер меньше одного кадра 20 мс.</exception>
    /// <exception cref="OperationCanceledException">Чтение отменено.</exception>
    ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken);

    /// <summary>Останавливает захват; повторный вызов идемпотентен.</summary>
    /// <param name="cancellationToken">Ограничение ожидания остановки.</param>
    Task StopAsync(CancellationToken cancellationToken);
}
