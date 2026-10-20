using BabyMonitor.Application.Abstractions.Realtime;

namespace BabyMonitor.Application.Tests.Realtime;

/// <summary>
/// Контракт адреса peer: guard-проверки хоста и порта, сохранение значений и семантика
/// record-equality. Endpoint не является identity устройства (AGENTS.md, раздел 8):
/// при смене сети адрес резолвится заново, поэтому проверяется только его транспортная валидность.
/// </summary>
public sealed class TransportEndpointTests
{
    [Fact]
    public void Создание_endpoint_с_null_хостом_бросает_ArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new TransportEndpoint(null!, 50000));

        Assert.Equal("host", exception.ParamName);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("   ")]
    [InlineData("\t")]
    [InlineData("\n")]
    public void Создание_endpoint_с_пустым_или_пробельным_хостом_бросает_ArgumentException(string host)
    {
        var exception = Assert.Throws<ArgumentException>(() => new TransportEndpoint(host, 50000));

        Assert.Equal("host", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void Создание_endpoint_с_портом_вне_диапазона_бросает_ArgumentOutOfRangeException(int port)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new TransportEndpoint("192.0.2.10", port));

        Assert.Equal("port", exception.ParamName);
        Assert.Equal(port, Assert.IsType<int>(exception.ActualValue));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(50000)]
    [InlineData(65535)]
    public void Создание_endpoint_с_валидным_портом_сохраняет_значения(int port)
    {
        var endpoint = new TransportEndpoint("192.0.2.10", port);

        Assert.Equal("192.0.2.10", endpoint.Host);
        Assert.Equal(port, endpoint.Port);
    }

    [Fact]
    public void Endpoint_сохраняет_хост_и_порт()
    {
        var endpoint = new TransportEndpoint("child-device.local", 50000);

        Assert.Equal("child-device.local", endpoint.Host);
        Assert.Equal(50000, endpoint.Port);
    }

    [Fact]
    public void Endpoint_является_record_с_равенством_по_хосту_и_порту()
    {
        var endpoint = new TransportEndpoint("192.0.2.10", 50000);
        var sameEndpoint = new TransportEndpoint("192.0.2.10", 50000);
        var otherPort = new TransportEndpoint("192.0.2.10", 50001);
        var otherHost = new TransportEndpoint("192.0.2.11", 50000);

        Assert.Equal(endpoint, sameEndpoint);
        Assert.Equal(endpoint.GetHashCode(), sameEndpoint.GetHashCode());
        Assert.True(endpoint == sameEndpoint);
        Assert.NotEqual(endpoint, otherPort);
        Assert.NotEqual(endpoint, otherHost);
        Assert.True(endpoint != otherPort);
    }
}
