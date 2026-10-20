namespace BabyMonitor.Application.Results;

/// <summary>
/// Типизированная ошибка приложения: стабильный машинно-читаемый код и безопасное
/// для показа пользователю сообщение. Технические детали и секреты в сообщение не попадают.
/// </summary>
public sealed record Error
{
    /// <summary>Создаёт ошибку приложения.</summary>
    /// <param name="code">Стабильный код ошибки, например <c>discovery.unavailable</c>.</param>
    /// <param name="message">Пользовательское сообщение без технических деталей и секретов.</param>
    /// <exception cref="ArgumentException">Код или сообщение пусты.</exception>
    public Error(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
    }

    /// <summary>Стабильный код ошибки.</summary>
    public string Code { get; }

    /// <summary>Пользовательское сообщение.</summary>
    public string Message { get; }
}
