namespace BabyMonitor.Application.Abstractions.Realtime;

/// <summary>
/// Нарушение контракта transport-сессии: попытка отправить аудио после Close или Failed.
/// Это ошибка программирования вызывающего, а не ожидаемый transport-отказ: ожидаемые отказы
/// возвращаются через <see cref="IRealtimeTransport.ConnectAsync"/> и <see cref="IRealtimeTransportSession.LastError"/>.
/// </summary>
public sealed class RealtimeTransportClosedException : InvalidOperationException
{
    private const string DefaultMessage = "Transport-сессия закрыта или завершилась ошибкой.";

    /// <summary>Создаёт исключение с сообщением по умолчанию.</summary>
    public RealtimeTransportClosedException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Создаёт исключение с пояснением.</summary>
    /// <param name="message">Описание нарушения контракта.</param>
    public RealtimeTransportClosedException(string message)
        : base(message)
    {
    }

    /// <summary>Создаёт исключение с пояснением и вложенной причиной.</summary>
    /// <param name="message">Описание нарушения контракта.</param>
    /// <param name="innerException">Вложенная причина.</param>
    public RealtimeTransportClosedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
