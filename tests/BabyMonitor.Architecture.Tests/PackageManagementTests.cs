using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>Центральное управление версиями NuGet-пакетов через Directory.Packages.props.</summary>
public sealed class PackageManagementTests
{
    private static readonly string[] TestProjectNames =
    [
        "BabyMonitor.Domain.Tests",
        "BabyMonitor.Application.Tests",
        "BabyMonitor.Architecture.Tests",
    ];

    private static readonly string[] PrivateTestPackages =
    [
        "coverlet.collector",
        "xunit.runner.visualstudio",
    ];

    [Fact]
    public void Directory_Packages_props_включает_центральное_управление_версиями()
    {
        var propertiesPath = ArchitectureTestSupport.GetPathFromRoot("Directory.Packages.props");
        Assert.True(File.Exists(propertiesPath), $"Файл центральных версий пакетов не найден: {propertiesPath}.");

        var actualValue = ArchitectureTestSupport.LoadXml("Directory.Packages.props")
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "ManagePackageVersionsCentrally")
            ?.Value.Trim();

        Assert.True(
            string.Equals(actualValue, "true", StringComparison.OrdinalIgnoreCase),
            $"Directory.Packages.props ({propertiesPath}): ожидалось ManagePackageVersionsCentrally=true, " +
            $"фактически {actualValue ?? "<не задано>"}.");
    }

    [Fact]
    public void Ни_один_csproj_не_задаёт_версию_PackageReference()
    {
        var root = ArchitectureTestSupport.FindRepositoryRoot();
        var violations = new List<string>();

        foreach (var projectPath in ArchitectureTestSupport.FindProjectFiles())
        {
            var packageReferences = XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference");

            foreach (var packageReference in packageReferences)
            {
                var version = (string?)packageReference.Attribute("Version");
                if (version is null)
                {
                    continue;
                }

                var include = (string?)packageReference.Attribute("Include") ?? "<без Include>";
                var relativePath = Path.GetRelativePath(root, projectPath).Replace('\\', '/');
                violations.Add($"{relativePath}: PackageReference '{include}' с Version='{version}'");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Версии пакетов задаются только в Directory.Packages.props (ManagePackageVersionsCentrally=true): " +
            string.Join("; ", violations));
    }

    [Fact]
    public void Тестовые_пакеты_имеют_PrivateAssets_all()
    {
        var violations = new List<string>();

        foreach (var projectName in TestProjectNames)
        {
            var projectPath = ArchitectureTestSupport.GetProjectFullPath(projectName);
            var packageReferences = XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .ToList();

            foreach (var packageName in PrivateTestPackages)
            {
                var packageReference = packageReferences.FirstOrDefault(
                    element => string.Equals((string?)element.Attribute("Include"), packageName, StringComparison.Ordinal));

                if (packageReference is null)
                {
                    violations.Add($"{projectName}.csproj: отсутствует PackageReference '{packageName}'");
                    continue;
                }

                var privateAssets = (string?)packageReference.Attribute("PrivateAssets");
                if (!string.Equals(privateAssets, "all", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add(
                        $"{projectName}.csproj ({projectPath}): '{packageName}' имеет PrivateAssets='{privateAssets ?? "<не задано>"}' вместо 'all'");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Тестовые пакеты coverlet.collector и xunit.runner.visualstudio должны иметь PrivateAssets=\"all\": " +
            string.Join("; ", violations));
    }
}
