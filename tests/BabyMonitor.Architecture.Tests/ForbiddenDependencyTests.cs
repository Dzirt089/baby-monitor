using System.Text;
using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Чистые слои Domain и Application не содержат платформенных, сетевых и инфраструктурных
/// зависимостей (AGENTS.md, разделы 5, 13 и 20).
/// </summary>
public sealed class ForbiddenDependencyTests
{
    private static readonly ForbiddenReference[] ForbiddenReferences =
    [
        new("Android.", "Android API разрешён только в Platform.Android"),
        new("Microsoft.Maui", "MAUI не входит в чистые слои"),
        new("System.Net.Sockets", "сокеты относятся к Infrastructure"),
        new("SIPSorcery", "media-библиотека относится к Infrastructure"),
        new("Microsoft.Extensions.Logging", "логирование в Domain/Application запрещено"),
        new("Microsoft.Extensions.DependencyInjection", "DI container в Domain/Application запрещён"),
        new("System.Text.Json", "JSON относится к адаптерам, а не к Domain/Application"),
        new("System.IO", "файловый ввод-вывод относится к Infrastructure"),
        new("FileStream", "файловый ввод-вывод относится к Infrastructure"),
        new("StreamReader", "файловый ввод-вывод относится к Infrastructure"),
        new("StreamWriter", "файловый ввод-вывод относится к Infrastructure"),
        new("File.", "прямая работа с файлами запрещена"),
        new("Directory.", "прямая работа с каталогами запрещена"),
        new("DateTime.Now", "системное время должно приходить через TimeProvider"),
        new("DateTime.UtcNow", "системное время должно приходить через TimeProvider"),
    ];

    [Theory]
    [InlineData("BabyMonitor.Domain")]
    [InlineData("BabyMonitor.Application")]
    public void В_чистом_слое_нет_запрещённых_зависимостей(string projectName)
    {
        var root = ArchitectureTestSupport.FindRepositoryRoot();
        var projectDirectory = ArchitectureTestSupport.GetProjectDirectory(projectName);
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(projectDirectory))
        {
            foreach (var codeLine in ReadCodeLines(filePath))
            {
                foreach (var forbiddenReference in ForbiddenReferences)
                {
                    if (!codeLine.Text.Contains(forbiddenReference.Token, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var relativePath = Path.GetRelativePath(root, filePath).Replace('\\', '/');
                    violations.Add($"{relativePath}:{codeLine.Number}: '{forbiddenReference.Token}' ({forbiddenReference.Reason})");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            $"В {projectName} найдены запрещённые зависимости: {string.Join("; ", violations)}. " +
            "Domain и Application используют только BCL и собственные контракты (AGENTS.md, разделы 5 и 20).");
    }

    [Theory]
    [InlineData("BabyMonitor.Domain")]
    [InlineData("BabyMonitor.Application")]
    public void В_чистом_слое_нет_PackageReference(string projectName)
    {
        var projectPath = ArchitectureTestSupport.GetProjectFullPath(projectName);
        var packages = XDocument.Load(projectPath)
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageReference")
            .Select(element => (string?)element.Attribute("Include") ?? "<без Include>")
            .ToList();

        Assert.True(
            packages.Count == 0,
            $"{projectName}.csproj ({projectPath}): чистые слои не должны содержать PackageReference; " +
            $"найдено: {ArchitectureTestSupport.FormatList(packages)}.");
    }

    /// <summary>Читает файл построчно, убирая комментарии, чтобы не ловить упоминания в документации.</summary>
    private static IReadOnlyList<CodeLine> ReadCodeLines(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var codeLines = new List<CodeLine>(lines.Length);
        var inBlockComment = false;

        for (var index = 0; index < lines.Length; index++)
        {
            var text = RemoveComments(lines[index], ref inBlockComment);
            codeLines.Add(new CodeLine(index + 1, text));
        }

        return codeLines;
    }

    private static string RemoveComments(string line, ref bool inBlockComment)
    {
        var result = new StringBuilder(line.Length);
        var position = 0;

        while (position < line.Length)
        {
            if (inBlockComment)
            {
                var blockEnd = line.IndexOf("*/", position, StringComparison.Ordinal);
                if (blockEnd < 0)
                {
                    return result.ToString();
                }

                position = blockEnd + 2;
                inBlockComment = false;
                continue;
            }

            var lineComment = line.IndexOf("//", position, StringComparison.Ordinal);
            var blockStart = line.IndexOf("/*", position, StringComparison.Ordinal);

            if (lineComment < 0 && blockStart < 0)
            {
                result.Append(line, position, line.Length - position);
                break;
            }

            if (lineComment >= 0 && (blockStart < 0 || lineComment < blockStart))
            {
                result.Append(line, position, lineComment - position);
                break;
            }

            result.Append(line, position, blockStart - position);
            position = blockStart + 2;
            inBlockComment = true;
        }

        return result.ToString();
    }

    private sealed record ForbiddenReference(string Token, string Reason);

    private sealed record CodeLine(int Number, string Text);
}
