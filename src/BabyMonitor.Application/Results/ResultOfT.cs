namespace BabyMonitor.Application.Results;

/// <summary>Результат use case с полезным значением при успешном исходе.</summary>
/// <typeparam name="T">Тип полезного значения.</typeparam>
public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error)
        : base(isSuccess, error)
    {
        _value = value;
    }

    /// <summary>Полезное значение успешного результата.</summary>
    /// <exception cref="InvalidOperationException">Результат неуспешен и значения не содержит.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Неуспешный результат не содержит значения.");

    /// <summary>Создаёт успешный результат со значением.</summary>
    /// <param name="value">Полезное значение; <see langword="null"/> не допускается.</param>
    /// <returns>Успешный результат.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="value"/> равен <see langword="null"/>; отсутствие значения моделируется
    /// отдельным контрактом, а не успешным результатом с null.
    /// </exception>
    public static Result<T> Success(T value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        return new(true, value, null);
    }

    /// <summary>Создаёт неуспешный результат с типизированной ошибкой.</summary>
    /// <param name="error">Причина неуспеха.</param>
    /// <returns>Неуспешный результат.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="error"/> равен <see langword="null"/>.</exception>
    public static new Result<T> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(false, default, error);
    }
}
