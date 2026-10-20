namespace BabyMonitor.Architecture.Tests;

/// <summary>Целевые платформы и Android-параметры проектов (AGENTS.md, раздел 4; ADR-0003).</summary>
public sealed class TargetFrameworkTests
{
    [Theory]
    [InlineData("BabyMonitor.Domain")]
    [InlineData("BabyMonitor.Application")]
    [InlineData("BabyMonitor.Infrastructure")]
    public void Слой_таргетит_net10_0(string projectName)
    {
        AssertProjectProperty(projectName, "TargetFramework", "net10.0");
    }

    [Theory]
    [InlineData("BabyMonitor.Platform.Android")]
    [InlineData("BabyMonitor.App")]
    public void Android_проект_таргетит_net10_0_android(string projectName)
    {
        AssertProjectProperty(projectName, "TargetFramework", "net10.0-android");
    }

    [Fact]
    public void App_является_исполняемым_проектом()
    {
        AssertProjectProperty("BabyMonitor.App", "OutputType", "Exe");
    }

    [Fact]
    public void App_имеет_ApplicationId_com_example_babymonitor()
    {
        AssertProjectProperty("BabyMonitor.App", "ApplicationId", "com.example.babymonitor");
    }

    [Theory]
    [InlineData("BabyMonitor.Platform.Android")]
    [InlineData("BabyMonitor.App")]
    public void Android_проект_поддерживает_минимальный_API_24(string projectName)
    {
        AssertProjectProperty(projectName, "SupportedOSPlatformVersion", "24");
    }

    private static void AssertProjectProperty(string projectName, string propertyName, string expectedValue)
    {
        var projectPath = ArchitectureTestSupport.GetProjectFullPath(projectName);
        var actualValue = ArchitectureTestSupport.LoadProject(projectName)
            .Descendants()
            .Where(element => element.Name.LocalName == "PropertyGroup")
            .SelectMany(group => group.Elements())
            .LastOrDefault(element => element.Name.LocalName == propertyName)
            ?.Value.Trim();

        Assert.True(
            string.Equals(expectedValue, actualValue, StringComparison.Ordinal),
            $"{projectName}.csproj ({projectPath}): ожидалось {propertyName}={expectedValue}, " +
            $"фактически {propertyName}={actualValue ?? "<не задано>"}.");
    }
}
