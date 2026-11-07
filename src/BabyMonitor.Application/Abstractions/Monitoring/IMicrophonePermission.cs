using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Abstractions.Monitoring;

/// <summary>
/// Порт разрешения на микрофон (RECORD_AUDIO). Нужен роли Child как prerequisite для
/// foreground service типа microphone; Parent этот порт не использует и разрешение не запрашивает.
/// В 003 разрешение только запрашивается: AudioRecord не открывается до 005. Запрос выполняется
/// на всех поддерживаемых API (не только 34+): единый permission-flow Child и prerequisite 005.
/// </summary>
public interface IMicrophonePermission
{
    /// <summary>
    /// Проверяет и при необходимости запрашивает разрешение на микрофон.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания ответа пользователя.</param>
    /// <returns>
    /// Успех (true) — разрешение предоставлено; отказ — типизированная ошибка
    /// <see cref="ForegroundSessionErrorCodes.MicrophonePermissionDenied"/>.
    /// </returns>
    Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken);
}
