using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Tests.Results;

/// <summary>Контракт результата use case без полезного значения.</summary>
public sealed class ResultTests
{
    [Fact]
    public void Успешный_результат_не_содержит_ошибки()
    {
        var result = Result.Success();

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Неуспешный_результат_сохраняет_переданную_ошибку()
    {
        var error = new Error("discovery.unavailable", "Детское устройство не найдено.");

        var result = Result.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
    }

    [Fact]
    public void Result_Failure_с_null_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Result.Failure(null!));

        Assert.Equal("error", exception.ParamName);
    }
}
