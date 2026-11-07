namespace BabyMonitor.Domain.Sessions;

/// <summary>Нарушение инварианта/перехода состояния MonitoringSession.</summary>
public sealed class InvalidSessionTransitionException : Exception
{
    /// <summary>Создаёт исключение с сообщением.</summary>
    /// <param name="message">Описание нарушения.</param>
    public InvalidSessionTransitionException(string message)
        : base(message)
    {
    }
}
