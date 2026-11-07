using System.Text;
using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Политика QR pairing UI (006 phase 2, ADR-0011): ZXing.Net и camera API изолированы в
/// Platform.Android; CAMERA объявлен в manifest и не выходит за pairing-экран; QR payload и
/// verification code не попадают в логи.
/// </summary>
public sealed class QrPairingArchitectureTests
{
    private static readonly XNamespace AndroidNamespace = "http://schemas.android.com/apk/res/android";
    private static readonly string[] CameraApiTokens = ["CameraManager", "CameraDevice", "ImageReader"];
    private static readonly string[] SecretLogTokens = ["QrPayload", "ResponseQrPayload", "VerificationCode", "PairingInvitation", "PairingResponse"];

    [Fact]
    public void ZXing_Net_подключён_только_в_Platform_Android()
    {
        var violations = new List<string>();

        foreach (var projectPath in ArchitectureTestSupport.FindProjectFiles())
        {
            var references = XDocument.Load(projectPath)
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => (string?)element.Attribute("Include"))
                .Where(include => string.Equals(include, "ZXing.Net", StringComparison.Ordinal))
                .ToList();

            if (references.Count == 0)
            {
                continue;
            }

            var projectName = ArchitectureTestSupport.GetProjectName(projectPath);
            if (!string.Equals(projectName, "BabyMonitor.Platform.Android", StringComparison.Ordinal))
            {
                violations.Add(ArchitectureTestSupport.NormalizePath(projectPath));
            }
        }

        Assert.True(
            violations.Count == 0,
            "ZXing.Net допускается только в BabyMonitor.Platform.Android (ADR-0011); найден в: " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void ZXing_Net_зафиксирован_в_Directory_Packages_props()
    {
        var packageVersion = ArchitectureTestSupport.LoadXml("Directory.Packages.props")
            .Descendants()
            .SingleOrDefault(element =>
                element.Name.LocalName == "PackageVersion"
                && string.Equals((string?)element.Attribute("Include"), "ZXing.Net", StringComparison.Ordinal));

        Assert.NotNull(packageVersion);
        Assert.Equal("0.16.11", (string?)packageVersion!.Attribute("Version"));
    }

    [Fact]
    public void Camera_API_используется_только_в_Platform_Android_Qr()
    {
        var allowedDirectory = ArchitectureTestSupport.NormalizePath(
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Platform.Android/Qr"));
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            if (filePath.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = ReadCodeWithoutComments(filePath);
            foreach (var token in CameraApiTokens)
            {
                if (code.Contains(token, StringComparison.Ordinal))
                {
                    violations.Add($"{Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath)}: {token}");
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Camera2 API допускается только в Platform.Android/Qr (006 phase 2): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void Manifest_объявляет_CAMERA_и_необязательную_камеру()
    {
        var manifest = XDocument.Load(ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.App/AndroidManifest.xml"));
        var permissions = manifest.Descendants()
            .Where(element => element.Name.LocalName == "uses-permission")
            .Select(element => (string?)element.Attribute(AndroidNamespace + "name"))
            .ToList();

        Assert.Contains("android.permission.CAMERA", permissions, StringComparer.Ordinal);

        var cameraFeature = manifest.Descendants()
            .Where(element => element.Name.LocalName == "uses-feature")
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute(AndroidNamespace + "name"),
                    "android.hardware.camera.any",
                    StringComparison.Ordinal));

        Assert.NotNull(cameraFeature);
        Assert.Equal("false", (string?)cameraFeature!.Attribute(AndroidNamespace + "required"));
    }

    [Fact]
    public void Секреты_pairing_не_попадают_в_логи()
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

                foreach (var token in SecretLogTokens)
                {
                    if (line.Contains(token, StringComparison.Ordinal))
                    {
                        violations.Add($"{Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath)}:{index + 1}: {token}");
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "QR payload/verification code не должны логироваться (FR-016): " +
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
