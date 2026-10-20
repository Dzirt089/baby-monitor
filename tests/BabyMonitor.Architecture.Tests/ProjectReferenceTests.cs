namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Направление зависимостей между проектами solution. Источник истины — AGENTS.md,
/// раздел 5, и архитектурный skeleton bootstrap-промпта.
/// </summary>
public sealed class ProjectReferenceTests
{
    [Fact]
    public void Domain_не_ссылается_на_другие_проекты()
    {
        AssertProjectReferences("BabyMonitor.Domain");
    }

    [Fact]
    public void Application_ссылается_только_на_Domain()
    {
        AssertProjectReferences("BabyMonitor.Application", "BabyMonitor.Domain");
    }

    [Fact]
    public void Infrastructure_ссылается_только_на_Application_и_Domain()
    {
        AssertProjectReferences("BabyMonitor.Infrastructure", "BabyMonitor.Application", "BabyMonitor.Domain");
    }

    [Fact]
    public void Platform_Android_ссылается_только_на_Application_и_Infrastructure()
    {
        AssertProjectReferences("BabyMonitor.Platform.Android", "BabyMonitor.Application", "BabyMonitor.Infrastructure");
    }

    [Fact]
    public void App_ссылается_только_на_Application_Infrastructure_и_Platform_Android()
    {
        AssertProjectReferences("BabyMonitor.App", "BabyMonitor.Application", "BabyMonitor.Infrastructure", "BabyMonitor.Platform.Android");
    }

    [Fact]
    public void App_содержит_ProjectReference_на_все_три_проекта()
    {
        var projectPath = ArchitectureTestSupport.GetProjectFullPath("BabyMonitor.App");
        var actualReferenceNames = ArchitectureTestSupport.GetReferencedProjectPaths(projectPath)
            .Select(ArchitectureTestSupport.GetProjectName)
            .ToList();

        string[] expectedReferenceNames = ["BabyMonitor.Application", "BabyMonitor.Infrastructure", "BabyMonitor.Platform.Android"];
        var missing = expectedReferenceNames.Where(name => !actualReferenceNames.Contains(name, StringComparer.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0,
            $"BabyMonitor.App.csproj ({projectPath}) должен ссылаться на {ArchitectureTestSupport.FormatList(expectedReferenceNames)}; " +
            $"фактически {ArchitectureTestSupport.FormatList(actualReferenceNames)}; отсутствуют {ArchitectureTestSupport.FormatList(missing)}.");
    }

    [Fact]
    public void Тестовые_проекты_ссылаются_только_на_проекты_из_src()
    {
        var root = ArchitectureTestSupport.FindRepositoryRoot();
        string[] testProjectNames =
        [
            "BabyMonitor.Domain.Tests",
            "BabyMonitor.Application.Tests",
            "BabyMonitor.Architecture.Tests",
            "BabyMonitor.Infrastructure.Tests",
        ];
        var violations = new List<string>();

        foreach (var projectName in testProjectNames)
        {
            var projectPath = ArchitectureTestSupport.GetProjectFullPath(projectName);

            foreach (var referencePath in ArchitectureTestSupport.GetReferencedProjectPaths(projectPath))
            {
                var relativePath = Path.GetRelativePath(root, referencePath).Replace('\\', '/');
                if (!relativePath.StartsWith("src/", StringComparison.Ordinal))
                {
                    violations.Add($"{projectName}.csproj ссылается на не-src проект: {relativePath}");
                }
                else if (!File.Exists(referencePath))
                {
                    violations.Add($"{projectName}.csproj ссылается на отсутствующий файл: {relativePath}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Тестовые проекты должны ссылаться только на существующие проекты из src: " + string.Join("; ", violations));
    }

    [Fact]
    public void Infrastructure_Tests_ссылается_только_на_Application_и_Infrastructure()
    {
        AssertProjectReferences(
            "BabyMonitor.Infrastructure.Tests",
            "BabyMonitor.Application",
            "BabyMonitor.Infrastructure");
    }

    private static void AssertProjectReferences(string projectName, params string[] expectedReferenceNames)
    {
        var projectPath = ArchitectureTestSupport.GetProjectFullPath(projectName);
        var actualReferenceNames = ArchitectureTestSupport.GetReferencedProjectPaths(projectPath)
            .Select(ArchitectureTestSupport.GetProjectName)
            .ToList();

        var expectedNames = new HashSet<string>(expectedReferenceNames, StringComparer.Ordinal);
        var missing = expectedReferenceNames.Where(name => !actualReferenceNames.Contains(name, StringComparer.Ordinal)).ToList();
        var unexpected = actualReferenceNames.Where(name => !expectedNames.Contains(name)).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"{projectName}.csproj ({projectPath}): ожидались ProjectReference {ArchitectureTestSupport.FormatList(expectedReferenceNames)}, " +
            $"фактически {ArchitectureTestSupport.FormatList(actualReferenceNames)}; " +
            $"отсутствуют {ArchitectureTestSupport.FormatList(missing)}; лишние {ArchitectureTestSupport.FormatList(unexpected)}.");
    }
}
