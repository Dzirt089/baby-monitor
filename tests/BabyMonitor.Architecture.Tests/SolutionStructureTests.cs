namespace BabyMonitor.Architecture.Tests;

/// <summary>Согласованность BabyMonitor.slnx, каталогов src/tests и файловой системы.</summary>
public sealed class SolutionStructureTests
{
    private static readonly string[] ExpectedProjectNames =
    [
        "BabyMonitor.Domain",
        "BabyMonitor.Application",
        "BabyMonitor.Infrastructure",
        "BabyMonitor.Platform.Android",
        "BabyMonitor.App",
        "BabyMonitor.Domain.Tests",
        "BabyMonitor.Application.Tests",
        "BabyMonitor.Architecture.Tests",
        "BabyMonitor.Infrastructure.Tests",
    ];

    [Fact]
    public void Solution_содержит_ровно_девять_ожидаемых_проектов()
    {
        var declaredProjectNames = ArchitectureTestSupport.LoadSolutionProjects()
            .Select(project => ArchitectureTestSupport.GetProjectName(project.DeclaredPath))
            .ToList();

        var missing = ExpectedProjectNames.Where(name => !declaredProjectNames.Contains(name, StringComparer.Ordinal)).ToList();
        var unexpected = declaredProjectNames.Where(name => !ExpectedProjectNames.Contains(name, StringComparer.Ordinal)).ToList();

        Assert.True(
            missing.Count == 0 && unexpected.Count == 0,
            $"BabyMonitor.slnx: ожидались проекты {ArchitectureTestSupport.FormatList(ExpectedProjectNames)}, " +
            $"фактически {ArchitectureTestSupport.FormatList(declaredProjectNames)}; " +
            $"отсутствуют {ArchitectureTestSupport.FormatList(missing)}; лишние {ArchitectureTestSupport.FormatList(unexpected)}.");
    }

    [Fact]
    public void Все_проекты_solution_существуют_на_диске()
    {
        var root = ArchitectureTestSupport.FindRepositoryRoot();
        var missing = ArchitectureTestSupport.LoadSolutionProjects()
            .Where(project => !File.Exists(project.FullPath))
            .Select(project => project.DeclaredPath)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"BabyMonitor.slnx ссылается на отсутствующие проекты: {ArchitectureTestSupport.FormatList(missing)}. " +
            $"Корень репозитория: {root}.");
    }

    [Fact]
    public void Все_csproj_из_src_и_tests_включены_в_solution()
    {
        var root = ArchitectureTestSupport.FindRepositoryRoot();
        var solutionProjectPaths = ArchitectureTestSupport.LoadSolutionProjects()
            .Select(project => project.FullPath)
            .ToHashSet(ArchitectureTestSupport.PathComparer);

        var missingInSolution = ArchitectureTestSupport.FindProjectFiles()
            .Where(projectPath => !solutionProjectPaths.Contains(projectPath))
            .Select(projectPath => Path.GetRelativePath(root, projectPath).Replace('\\', '/'))
            .ToList();

        Assert.True(
            missingInSolution.Count == 0,
            "В BabyMonitor.slnx не включены csproj из src/ и tests/: " + ArchitectureTestSupport.FormatList(missingInSolution));
    }
}
