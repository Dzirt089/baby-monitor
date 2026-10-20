using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Tests.Results;

/// <summary>Контракт типизированной ошибки приложения: валидация входа и сохранение значений.</summary>
public sealed class ErrorTests
{
    [Fact]
    public void Создание_ошибки_с_null_кодом_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Error(null!, "message"));

        Assert.Equal("code", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Создание_ошибки_с_пустым_или_пробельным_кодом_бросает_ArgumentException(string code)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Error(code, "message"));

        Assert.Equal("code", exception.ParamName);
    }

    [Fact]
    public void Создание_ошибки_с_null_сообщением_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new Error("error.code", null!));

        Assert.Equal("message", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Создание_ошибки_с_пустым_или_пробельным_сообщением_бросает_ArgumentException(string message)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Error("error.code", message));

        Assert.Equal("message", exception.ParamName);
    }

    [Fact]
    public void Валидные_код_и_сообщение_сохраняются()
    {
        var error = new Error("discovery.unavailable", "Детское устройство не найдено.");

        Assert.Equal("discovery.unavailable", error.Code);
        Assert.Equal("Детское устройство не найдено.", error.Message);
    }
}
