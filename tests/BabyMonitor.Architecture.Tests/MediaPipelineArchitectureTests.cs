using System.Text;
using System.Text.RegularExpressions;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Границы 005/007: SIPSorcery только в Infrastructure, сетевые/TLS/HTTP API — только в
/// Infrastructure/Signaling|Realtime (signaling обязан быть TLS-only), аудио-данные не логируются
/// и не пишутся на диск.
/// </summary>
public sealed class MediaPipelineArchitectureTests
{
    private static readonly string[] AudioSecretLogTokens = ["opus", "pcm", "sdp", "payload", "secret"];

    private static readonly string[] FileWriteTokens = ["FileStream", "WriteAllBytes", "WriteAllText", "File."];

    /// <summary>
    /// Сетевые/HTTP/TLS-API, допустимые только в Infrastructure/Signaling|Realtime: signaling обязан
    /// быть TLS-only (ADR-0007), cleartext TCP/UDP/HTTP в других слоях запрещён. Токен Socket
    /// сопоставляется по границам слова (SocketException/WebSocketSharp/System.Net.Sockets — не Socket).
    /// </summary>
    private static readonly string[] ForbiddenNetworkTokens =
    [
        "Socket",
        "UdpClient",
        "TcpListener",
        "TcpClient",
        "SslStream",
        "SslClientAuthenticationOptions",
        "SslServerAuthenticationOptions",
        "HttpClient",
        "HttpMessageHandler",
        "WebRequest"
    ];

    // Эквивалент \bSocket\b без escape-последовательностей: Socket не внутри слова
    // (SocketException/WebSocketSharp/System.Net.Sockets не считаются типом Socket).
    private static readonly Regex SocketTypeToken = new(
        "(?<![A-Za-z0-9_])Socket(?![A-Za-z0-9_])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

    [Fact]
    public void Сетевые_и_TLS_API_только_в_Infrastructure_Signaling_Realtime()
    {
        var allowedDirectories = new[]
        {
            ArchitectureTestSupport.NormalizePath(
                ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Infrastructure/Signaling")),
            ArchitectureTestSupport.NormalizePath(
                ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Infrastructure/Realtime")),
        };
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            if (allowedDirectories.Any(directory => filePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath);
            var code = ReadCodeWithoutComments(filePath);
            foreach (var token in ForbiddenNetworkTokens)
            {
                if (ContainsForbiddenNetworkToken(code, token))
                {
                    violations.Add($"{relativePath}: {token}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Socket/UdpClient/TcpClient/TcpListener/SslStream/HttpClient/HttpMessageHandler/WebRequest " +
            "допускаются только в Infrastructure/Signaling|Realtime; signaling обязан быть TLS-only, " +
            "cleartext-соединения в других слоях запрещены (ADR-0007): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void Socket_токен_учитывает_границы_слов_и_комментарии_вырезаются()
    {
        // Положительные: тип Socket, в том числе fully-qualified.
        Assert.True(ContainsForbiddenNetworkToken(
            "var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);",
            "Socket"));
        Assert.True(ContainsForbiddenNetworkToken("System.Net.Sockets.Socket accepted;", "Socket"));

        // Отрицательные: SocketException/Sockets/WebSocketSharp не являются типом Socket.
        Assert.False(ContainsForbiddenNetworkToken("catch (SocketException exception)", "Socket"));
        Assert.False(ContainsForbiddenNetworkToken("using System.Net.Sockets;", "Socket"));
        Assert.False(ContainsForbiddenNetworkToken("var client = new WebSocketSharp.WebSocket(url);", "Socket"));

        // Комментарии и XML-doc вырезаются до поиска (как в остальных architecture-тестах):
        // doc-ссылки/упоминания не дают violation.
        var codeWithoutComments = JoinCodeWithoutComments(
        [
            "// Socket запрещён вне Infrastructure/Signaling",
            "/// <see cref=\"System.Net.Sockets.Socket\"/>",
            "var harmless = 1;"
        ]);
        Assert.False(ContainsForbiddenNetworkToken(codeWithoutComments, "Socket"));
    }

    /// <summary>Ищет запрещённый токен: Socket — по границам слова, остальные — как подстроку типа.</summary>
    private static bool ContainsForbiddenNetworkToken(string code, string token) =>
        string.Equals(token, "Socket", StringComparison.Ordinal)
            ? SocketTypeToken.IsMatch(code)
            : code.Contains(token, StringComparison.Ordinal);

    private static string ReadCodeWithoutComments(string filePath) =>
        JoinCodeWithoutComments(File.ReadAllLines(filePath));

    private static string JoinCodeWithoutComments(IEnumerable<string> lines)
    {
        var result = new StringBuilder();
        foreach (var line in lines)
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
