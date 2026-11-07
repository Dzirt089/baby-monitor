using BabyMonitor.Application.Monitoring;

namespace BabyMonitor.Application.Tests.Monitoring;

/// <summary>Политика reconnect: exponential backoff, детерминированный jitter, верхний предел.</summary>
public sealed class ReconnectPolicyTests
{
    [Fact]
    public void Первая_задержка_близка_к_initial_в_пределах_jitter()
    {
        var policy = new ReconnectPolicy(initialDelay: TimeSpan.FromMilliseconds(500), jitterRatio: 0.2);

        var delay = policy.GetDelay(1);

        Assert.InRange(delay.TotalMilliseconds, 400, 600);
    }

    [Fact]
    public void Задержка_растёт_с_номером_попытки()
    {
        var policy = new ReconnectPolicy(maxAttempts: 10, initialDelay: TimeSpan.FromMilliseconds(500), maxDelay: TimeSpan.FromSeconds(30), jitterRatio: 0);

        var first = policy.GetDelay(1);
        var second = policy.GetDelay(2);
        var third = policy.GetDelay(3);

        Assert.Equal(500, first.TotalMilliseconds);
        Assert.Equal(1000, second.TotalMilliseconds);
        Assert.Equal(2000, third.TotalMilliseconds);
    }

    [Fact]
    public void Задержка_не_превышает_maxDelay_даже_с_jitter()
    {
        var policy = new ReconnectPolicy(maxAttempts: 20, initialDelay: TimeSpan.FromMilliseconds(500), maxDelay: TimeSpan.FromSeconds(30), jitterRatio: 1.0);

        for (var attempt = 1; attempt <= 20; attempt++)
        {
            Assert.True(policy.GetDelay(attempt) <= TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public void Попытка_сверх_MaxAttempts_запрещена()
    {
        var policy = new ReconnectPolicy(maxAttempts: 3);

        Assert.True(policy.CanAttempt(1));
        Assert.True(policy.CanAttempt(3));
        Assert.False(policy.CanAttempt(0));
        Assert.False(policy.CanAttempt(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(4));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void NaN_и_бесконечность_в_factor_запрещены(double factor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy(factor: factor));
    }

    [Fact]
    public void NaN_в_jitter_запрещён()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy(jitterRatio: double.NaN));
    }

    [Fact]
    public void Jitter_детерминирован()
    {
        var policy = new ReconnectPolicy(jitterRatio: 0.5);

        Assert.Equal(policy.GetDelay(3), policy.GetDelay(3));
    }

    [Fact]
    public void Нулевой_jitter_даёт_точный_backoff()
    {
        var policy = new ReconnectPolicy(initialDelay: TimeSpan.FromMilliseconds(100), factor: 2, jitterRatio: 0);

        Assert.Equal(100, policy.GetDelay(1).TotalMilliseconds);
        Assert.Equal(200, policy.GetDelay(2).TotalMilliseconds);
        Assert.Equal(400, policy.GetDelay(3).TotalMilliseconds);
    }

    [Fact]
    public void Номер_попытки_меньше_единицы_запрещён()
    {
        var policy = new ReconnectPolicy();

        Assert.Throws<ArgumentOutOfRangeException>(() => policy.GetDelay(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Некорректный_maxAttempts_запрещён(int maxAttempts)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy(maxAttempts: maxAttempts));
    }

    [Fact]
    public void Jitter_больше_единицы_запрещён()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReconnectPolicy(jitterRatio: 1.5));
    }

    [Fact]
    public void MaxDelay_меньше_initial_запрещён()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ReconnectPolicy(initialDelay: TimeSpan.FromSeconds(5), maxDelay: TimeSpan.FromSeconds(1)));
    }
}
