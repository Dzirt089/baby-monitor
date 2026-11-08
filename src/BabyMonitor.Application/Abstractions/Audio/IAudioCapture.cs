using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Порт захвата микрофона (только Child). Parent этот порт не использует и RECORD_AUDIO не
/// запрашивает. Адаптер обязан открывать микрофон только после явного старта сессии, не писать
/// PCM на диск/в логи и освобождать ресурсы по Stop/Dispose.
/// </summary>
public interface IAudioCapture
{
    /// <summary>Запускает захват микрофона.</summary>
    /// <param name="options">Параметры захвата.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Сессия захвата либо типизированная ошибка (<see cref="AudioErrorCodes"/>).</returns>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<Result<IAudioCaptureSession>> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken);
}
