using System.Text;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Границы 005 phase 2: SIPSorcery только в Infrastructure, TLS-код только в Signaling-адаптере,
/// аудио-данные не логируются и не пишутся на диск.
/// </summary>
public sealed class MediaPipelineArchitectureTests
{
    private static readonly string[] AudioSecretLogTokens = ["opus", "pcm", "sdp", "payload", "secret"];

    private static readonly string[] FileWriteTokens = ["FileStream", "WriteAllBytes", "WriteAllText", "File."];

    [Fact]
    public void SIPSorcery_используется_только_в_Infrastructure()
    {
        var allowedDirectory = ArchitectureTestSupport.NormalizePath(
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Infrastructure"));
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            if (filePath.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (ReadCodeWithoutComments(filePath).Contains("SIPSorcery", StringComparison.Ordinal))
            {
                violations.Add(Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath));
            }
        }

        Assert.True(
            violations.Count == 0,
            "SIPSorcery допускается только в Infrastructure (порты Application не должны видеть media-библиотеку): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void TLS_validation_не_отключается_и_кастомная_валидация_только_в_Signaling()
    {
        var allowedDirectory = ArchitectureTestSupport.NormalizePath(
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Infrastructure/Signaling"));
        var forbiddenEverywhere = new[] { "DangerousAcceptAnyServerCertificateValidator", "ServerCertificateCustomValidationCallback" };
        var forbiddenOutsideSignaling = new[] { "RemoteCertificateValidationCallback" };
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            var relativePath = Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath);
            var code = ReadCodeWithoutComments(filePath);

            foreach (var token in forbiddenEverywhere)
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: {token}");
                }
            }

            if (filePath.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var token in forbiddenOutsideSignaling)
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{relativePath}: {token} (разрешено только в Infrastructure/Signaling)");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "TLS-валидация не должна отключаться; кастомный callback допустим только в Signaling-адаптере (pinning): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void Аудиоданные_и_SDP_не_попадают_в_логи()
    {
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            var lines = File.ReadAllLines(filePath);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (!line.Contains("Log.", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("//", StringComparison.Ordinal)
                    || line.TrimStart().StartsWith("*", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var token in AudioSecretLogTokens)
                {
                    if (line.Contains(token, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath)}:{index + 1}: {token}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Аудио/SDP/payload не должны логироваться (FR-017/FR-021): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void Аудио_адаптеры_не_пишут_данные_на_диск()
    {
        var directories = new[]
        {
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Platform.Android/Audio"),
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Infrastructure/Audio"),
        };
        var violations = new List<string>();

        foreach (var directory in directories)
        {
            foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(directory))
            {
                var code = ReadCodeWithoutComments(filePath);
                foreach (var token in FileWriteTokens)
                {
                    if (code.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath)}: {token}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Аудио не должно сохраняться на диск (AGENTS §7/§20): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    private static string ReadCodeWithoutComments(string filePath)
    {
        var result = new StringBuilder();
        foreach (var line in File.ReadAllLines(filePath))
        {
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal)
                || trimmed.StartsWith("*", StringComparison.Ordinal)
                || trimmed.StartsWith("/*", StringComparison.Ordinal))
            {
                continue;
            }

            result.AppendLine(line);
        }

        return result.ToString();
    }
}
