using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Domain.Tests.Updates;

/// <summary>
/// Сравнение версий приложения: обновлением считается только строго больший versionCode;
/// equal и downgrade запрещены, versionName на сравнение не влияет.
/// </summary>
public sealed class AppVersionTests
{
    [Theory]
    [InlineData(2, 1)]
    [InlineData(101, 100)]
    [InlineData(int.MaxValue, int.MaxValue - 1)]
    public void Более_высокий_versionCode_новее(int candidateCode, int currentCode)
    {
        var candidate = new AppVersion(candidateCode, "candidate");
        var current = new AppVersion(currentCode, "current");

        Assert.True(AppVersion.IsNewerThan(candidate, current));
        Assert.True(AppVersion.Compare(candidate, current) > 0);
        Assert.False(AppVersion.IsNewerThan(current, candidate));
        Assert.True(AppVersion.Compare(current, candidate) < 0);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    public void Равный_versionCode_не_является_обновлением(int leftCode, int rightCode)
    {
        var left = new AppVersion(leftCode, "left");
        var right = new AppVersion(rightCode, "right");

        Assert.False(AppVersion.IsNewerThan(left, right));
        Assert.False(AppVersion.IsNewerThan(right, left));
        Assert.Equal(0, AppVersion.Compare(left, right));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(99, 100)]
    public void Меньший_versionCode_не_является_обновлением(int candidateCode, int currentCode)
    {
        var candidate = new AppVersion(candidateCode, "old");
        var current = new AppVersion(currentCode, "new");

        Assert.False(AppVersion.IsNewerThan(candidate, current));
        Assert.True(AppVersion.Compare(candidate, current) < 0);
    }

    [Fact]
    public void VersionName_не_влияет_на_сравнение()
    {
        var candidate = new AppVersion(100, "9.9.9-not-a-number");
        var current = new AppVersion(100, "0.0.1");

        Assert.False(AppVersion.IsNewerThan(candidate, current));
        Assert.Equal(0, AppVersion.Compare(candidate, current));
    }

    [Fact]
    public void Значения_версии_доступны_для_отображения()
    {
        var version = new AppVersion(123, "0.5.1");

        Assert.Equal(123, version.VersionCode);
        Assert.Equal("0.5.1", version.VersionName);
    }
}
