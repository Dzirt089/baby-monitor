using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Ошибка выполнения аудио-адаптера после успешного старта сессии: потеря audio focus, отказ
/// устройства ввода-вывода или обрыв нативного чтения/записи. Ожидаемые отказы запуска
/// возвращаются через <see cref="Result{T}"/>; это исключение — отказ уже работающей сессии.
/// </summary>
/// <remarks>
/// Наследуется от <see cref="IOException"/>: Application-слой не ссылается на Platform.Android и
/// перехватывает отказ аудиоустройства как ошибку ввода-вывода на границе адаптера, преобразуя её
/// в типизированный <see cref="Error"/> с кодом из <see cref="AudioErrorCodes"/>. В исключении нет
/// аудиоданных, идентификаторов устройств и путей: только безопасные код и сообщение
/// (AGENTS.md §7, §12, §14).
/// </remarks>
public sealed class AndroidAudioAdapterException : IOException
{
    /// <summary>Создаёт ошибку аудио-адаптера.</summary>
    /// <param name="error">Типизированная причина отказа.</param>
    /// <param name="message">Безопасное техническое сообщение без аудиоданных и путей.</param>
    /// <param name="innerException">Исходное исключение платформы (для диагностики, без аудио).</param>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> равен <see langword="null"/>.</exception>
    public AndroidAudioAdapterException(Error error, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        ArgumentNullException.ThrowIfNull(error);
        Error = error;
    }

    /// <summary>Типизированная причина отказа с кодом из <see cref="AudioErrorCodes"/>.</summary>
    public Error Error { get; }
}
