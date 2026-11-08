using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>
/// Порт воспроизведения (только Parent). Child этот порт не использует и playback не создаёт.
/// Реализация отвечает за audio focus и routing (speaker/headset/Bluetooth), не пишет аудио на
/// диск/в логи и освобождает ресурсы по Stop/Dispose.
/// </summary>
public interface IAudioPlayback
{
    /// <summary>Запускает воспроизведение.</summary>
    /// <param name="options">Параметры воспроизведения.</param>
    /// <param name="cancellationToken">Отмена запуска.</param>
    /// <returns>Сессия воспроизведения либо типизированная ошибка (<see cref="AudioErrorCodes"/>).</returns>
    /// <exception cref="OperationCanceledException">Запуск отменён.</exception>
    Task<Result<IAudioPlaybackSession>> StartAsync(
        AudioPlaybackOptions options,
        CancellationToken cancellationToken);
}
