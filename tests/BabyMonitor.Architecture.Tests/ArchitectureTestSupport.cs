using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Вспомогательные операции архитектурных тестов: поиск корня репозитория, чтение
/// solution и csproj, перечисление исходников. Только BCL, без внешних библиотек.
/// </summary>
internal static class ArchitectureTestSupport
{
    private const string SolutionFileName = "BabyMonitor.slnx";

    private static readonly string[] ProjectAreas = ["src", "tests"];

    /// <summary>
    /// Сравнение путей с учётом файловой системы: Windows регистронезависим, Linux — нет.
    /// </summary>
    public static StringComparer PathComparer { get; } =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Поднимается от каталога тестовой сборки вверх до каталога, содержащего BabyMonitor.slnx.
    /// </summary>
    /// <exception cref="InvalidOperationException">Корень репозитория не найден.</exception>
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var solutionPath = Path.Combine(directory.FullName, SolutionFileName);
            if (File.Exists(solutionPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Не удалось найти корень репозитория: файл {SolutionFileName} отсутствует во всех родительских каталогах от '{AppContext.BaseDirectory}'.");
    }

    public static string GetPathFromRoot(string relativePath) =>
        Path.Combine(FindRepositoryRoot(), NormalizeSeparators(relativePath));

    public static XDocument LoadXml(string relativePath) => XDocument.Load(GetPathFromRoot(relativePath));

    public static string GetProjectFullPath(string projectName)
    {
        var root = FindRepositoryRoot();
        var candidates = new[]
        {
            Path.Combine(root, "src", projectName, projectName + ".csproj"),
            Path.Combine(root, "tests", projectName, projectName + ".csproj"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return NormalizePath(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Не найден csproj проекта '{projectName}'. Проверенные пути: {FormatList(candidates)}.");
    }

    public static string GetProjectDirectory(string projectName)
    {
        var projectPath = GetProjectFullPath(projectName);
        return Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"Не удалось определить каталог проекта: {projectPath}.");
    }

    public static XDocument LoadProject(string projectName) => XDocument.Load(GetProjectFullPath(projectName));

    /// <summary>Имя проекта по относительному или абсолютному пути к csproj.</summary>
    public static string GetProjectName(string projectPath) =>
        Path.GetFileNameWithoutExtension(NormalizeSeparators(projectPath));

    /// <summary>Проекты, объявленные в BabyMonitor.slnx, с абсолютными путями.</summary>
    public static IReadOnlyList<ProjectEntry> LoadSolutionProjects()
    {
        var root = FindRepositoryRoot();
        var solutionPath = Path.Combine(root, SolutionFileName);
        if (!File.Exists(solutionPath))
        {
            throw new InvalidOperationException($"Файл solution не найден: {solutionPath}.");
        }

        var entries = new List<ProjectEntry>();
        var document = XDocument.Load(solutionPath);

        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "Project"))
        {
            var declaredPath = (string?)element.Attribute("Path");
            if (string.IsNullOrWhiteSpace(declaredPath))
            {
                throw new InvalidOperationException($"В {solutionPath} найден элемент Project без атрибута Path.");
            }

            entries.Add(new ProjectEntry(declaredPath, NormalizePath(Path.Combine(root, NormalizeSeparators(declaredPath)))));
        }

        return entries;
    }

    /// <summary>Абсолютные пути ProjectReference, объявленных в указанном csproj.</summary>
    public static IReadOnlyList<string> GetReferencedProjectPaths(string projectFullPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectFullPath)
            ?? throw new InvalidOperationException($"Не удалось определить каталог проекта: {projectFullPath}.");

        var references = new List<string>();
        var document = XDocument.Load(projectFullPath);

        foreach (var element in document.Descendants().Where(element => element.Name.LocalName == "ProjectReference"))
        {
            var include = (string?)element.Attribute("Include");
            if (string.IsNullOrWhiteSpace(include))
            {
                throw new InvalidOperationException($"В {projectFullPath} найден ProjectReference без атрибута Include.");
            }

            references.Add(NormalizePath(Path.Combine(projectDirectory, NormalizeSeparators(include))));
        }

        return references;
    }

    /// <summary>Все csproj из src/ и tests/, исключая build-каталоги bin/obj.</summary>
    public static IReadOnlyList<string> FindProjectFiles()
    {
        var projectFiles = new List<string>();

        foreach (var area in ProjectAreas)
        {
            var areaDirectory = Path.Combine(FindRepositoryRoot(), area);
            if (!Directory.Exists(areaDirectory))
            {
                continue;
            }

            projectFiles.AddRange(
                Directory.EnumerateFiles(areaDirectory, "*.csproj", SearchOption.AllDirectories)
                    .Where(path => !IsBuildOutputPath(path))
                    .Select(NormalizePath));
        }

        return projectFiles;
    }

    /// <summary>Все .cs-файлы проекта, исключая build-каталоги bin/obj.</summary>
    public static IReadOnlyList<string> FindCSharpFiles(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        return Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutputPath(path))
            .Select(NormalizePath)
            .ToList();
    }

    public static string NormalizePath(string path) =>
        Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    /// <summary>Форматирует список значений для сообщения о падении теста.</summary>
    public static string FormatList(IEnumerable<string> values) => $"[{string.Join(", ", values)}]";

    private static string NormalizeSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    private static bool IsBuildOutputPath(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin", StringComparer.Ordinal) || segments.Contains("obj", StringComparer.Ordinal);
    }
}

/// <summary>Проект, объявленный в solution-файле.</summary>
internal sealed record ProjectEntry(string DeclaredPath, string FullPath);
