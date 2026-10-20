using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Tests.Results;

/// <summary>Контракт результата use case с полезным значением: value type и reference type.</summary>
public sealed class ResultOfTTests
{
    [Fact]
    public void Успешный_результат_с_типом_int_возвращает_значение()
    {
        var result = Result<int>.Success(42);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void Успешный_результат_со_строкой_возвращает_значение()
    {
        var result = Result<string>.Success("audio-profile");

        Assert.True(result.IsSuccess);
        Assert.Null(result.Error);
        Assert.Equal("audio-profile", result.Value);
    }

    [Fact]
    public void Result_со_строкой_и_null_значением_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Result<string>.Success(null!));

        Assert.Equal("value", exception.ParamName);
    }

    [Fact]
    public void Неуспешный_результат_с_типом_int_бросает_при_обращении_к_значению()
    {
        var error = new Error("capture.unavailable", "Микрофон недоступен.");
        var result = Result<int>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => { _ = result.Value; });
    }

    [Fact]
    public void Неуспешный_результат_со_строкой_бросает_при_обращении_к_значению()
    {
        var error = new Error("playback.unavailable", "Устройство воспроизведения недоступно.");
        var result = Result<string>.Failure(error);

        Assert.False(result.IsSuccess);
        Assert.Same(error, result.Error);
        Assert.Throws<InvalidOperationException>(() => { _ = result.Value; });
    }

    [Fact]
    public void Result_типа_int_с_null_ошибкой_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => Result<int>.Failure(null!));

        Assert.Equal("error", exception.ParamName);
    }
}
