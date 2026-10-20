namespace BabyMonitor.Application.Results;

/// <summary>
/// Результат use case без полезного значения: успех либо типизированная ошибка.
/// Ожидаемые исходы не выражаются исключениями (AGENTS.md, раздел 12).
/// </summary>
public class Result
{
    private protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
        {
            throw new InvalidOperationException("Успешный результат не может содержать ошибку.");
        }

        if (!isSuccess && error is null)
        {
            throw new InvalidOperationException("Неуспешный результат обязан содержать ошибку.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Признак успешного исхода.</summary>
    public bool IsSuccess { get; }

    /// <summary>Ошибка при неуспешном исходе; <see langword="null"/> при успехе.</summary>
    public Error? Error { get; }

    /// <summary>Создаёт успешный результат.</summary>
    /// <returns>Успешный результат без значения.</returns>
    public static Result Success() => new(true, null);

    /// <summary>Создаёт неуспешный результат с типизированной ошибкой.</summary>
    /// <param name="error">Причина неуспеха.</param>
    /// <returns>Неуспешный результат.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> равен <see langword="null"/>.</exception>
    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, error);
    }
}
