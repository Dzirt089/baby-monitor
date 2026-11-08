namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Signaling-соединение закрыто peer-ом или локально. Ожидаемый отказ transport-уровня, а не ошибка
/// программирования: media-адаптер преобразует его в типизированную ошибку сессии.
/// </summary>
public sealed class SignalingConnectionClosedException : IOException
{
    /// <summary>Создаёт исключение с пояснением.</summary>
    /// <param name="message">Описание отказа.</param>
    public SignalingConnectionClosedException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт исключение с пояснением и вложенной причиной.</summary>
    /// <param name="message">Описание отказа.</param>
    /// <param name="innerException">Вложенная причина.</param>
    public SignalingConnectionClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Нарушение signaling-протокола: неизвестный тип кадра, превышение лимита размера, обрезанный
/// кадр или некорректный payload. Ожидаемый отказ недоверенного peer-а, не ошибка программирования.
/// </summary>
public sealed class SignalingProtocolException : IOException
{
    /// <summary>Создаёт исключение с пояснением.</summary>
    /// <param name="message">Описание нарушения протокола.</param>
    public SignalingProtocolException(string message)
        : base(message)
    {
    }
}
