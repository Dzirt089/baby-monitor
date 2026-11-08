using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Гигиена AndroidManifest: минимальные разрешения для discovery (002), foreground monitoring (003)
/// и pairing-камеры (006 phase 2); отсутствие microphone permissions сверх явно необходимого
/// (Parent не должен получать RECORD_AUDIO «на всякий случай»).
/// </summary>
public sealed class AndroidManifestTests
{
    private static readonly XNamespace AndroidNamespace = "http://schemas.android.com/apk/res/android";

    private static readonly string[] ExpectedPermissions =
    [
        "android.permission.INTERNET",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.CHANGE_WIFI_MULTICAST_STATE",
        "android.permission.ACCESS_LOCAL_NETWORK",
        "android.permission.FOREGROUND_SERVICE",
        "android.permission.FOREGROUND_SERVICE_MICROPHONE",
        "android.permission.FOREGROUND_SERVICE_MEDIA_PLAYBACK",
        "android.permission.POST_NOTIFICATIONS",
        "android.permission.RECORD_AUDIO",
        "android.permission.CAMERA",
    ];

    private static readonly string[] ForbiddenPermissions =
    [
        "android.permission.CAPTURE_AUDIO_OUTPUT",
        "android.permission.MODIFY_AUDIO_SETTINGS",
    ];

    [Fact]
    public void Manifest_содержит_только_ожидаемые_permissions()
    {
        var manifest = LoadManifest();
        var permissions = ReadPermissions(manifest);

        var unexpected = permissions.Except(ExpectedPermissions, StringComparer.Ordinal).ToList();
        var missing = ExpectedPermissions.Except(permissions, StringComparer.Ordinal).ToList();

        Assert.True(
            unexpected.Count == 0 && missing.Count == 0,
            $"Ожидались permissions {ArchitectureTestSupport.FormatList(ExpectedPermissions)}; " +
            $"фактически {ArchitectureTestSupport.FormatList(permissions)}; " +
            $"лишние {ArchitectureTestSupport.FormatList(unexpected)}; отсутствуют {ArchitectureTestSupport.FormatList(missing)}.");
    }

    [Fact]
    public void Manifest_не_запрашивает_microphone_permissions()
    {
        var permissions = ReadPermissions(LoadManifest());

        var forbidden = permissions.Intersect(ForbiddenPermissions, StringComparer.Ordinal).ToList();

        Assert.True(
            forbidden.Count == 0,
            "Discovery не должен добавлять microphone/audio permissions: " + ArchitectureTestSupport.FormatList(forbidden));
    }


    [Fact]
    public void Merged_manifest_объявляет_foreground_service_с_типами_microphone_и_mediaPlayback()
    {
        var candidates = new[]
        {
            "src/BabyMonitor.App/obj/Debug/net10.0-android/AndroidManifest.xml",
            "src/BabyMonitor.App/obj/Release/net10.0-android/AndroidManifest.xml",
        }
        .Select(relative => ArchitectureTestSupport.GetPathFromRoot(relative))
        .Where(File.Exists)
        .ToArray();
        Assert.True(candidates.Length > 0, "Merged AndroidManifest.xml не найден: architecture test требует предварительного build.");

        XDocument? merged = null;
        foreach (var path in candidates)
        {
            var document = XDocument.Load(path);
            var hasService = document.Descendants()
                .Any(element => element.Name.LocalName == "service"
                    && ((string?)element.Attribute(AndroidNamespace + "name"))?.EndsWith("MonitoringForegroundService", StringComparison.Ordinal) == true);
            if (hasService)
            {
                merged = document;
                break;
            }
        }

        Assert.NotNull(merged);
        var service = merged!.Descendants()
            .Single(element => element.Name.LocalName == "service"
                && ((string?)element.Attribute(AndroidNamespace + "name"))!.EndsWith("MonitoringForegroundService", StringComparison.Ordinal));
        Assert.Equal("false", (string?)service.Attribute(AndroidNamespace + "exported"));
        var serviceType = (string?)service.Attribute(AndroidNamespace + "foregroundServiceType") ?? string.Empty;
        Assert.Contains("microphone", serviceType, StringComparison.Ordinal);
        Assert.Contains("mediaPlayback", serviceType, StringComparison.Ordinal);
    }

    [Fact]
    public void Android_audio_apis_используются_только_в_Platform_Android_Audio()
    {
        // 005 phase 2: AudioRecord/AudioTrack разрешены только в platform-адаптерах Audio;
        // Application/Domain/App их не видят (порты IAudioCapture/IAudioPlayback).
        var allowedDirectory = ArchitectureTestSupport.NormalizePath(
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Platform.Android/Audio"));
        var violations = new List<string>();

        foreach (var filePath in ArchitectureTestSupport.FindCSharpFiles(ArchitectureTestSupport.GetPathFromRoot("src")))
        {
            var code = string.Join(
                '\n',
                File.ReadAllLines(filePath).Where(line =>
                {
                    var trimmed = line.TrimStart();
                    return !trimmed.StartsWith("//", StringComparison.Ordinal)
                        && !trimmed.StartsWith("*", StringComparison.Ordinal)
                        && !trimmed.StartsWith("/*", StringComparison.Ordinal);
                }));

            var containsAudioApi = code.Contains("AudioRecord", StringComparison.Ordinal)
                || code.Contains("AudioTrack", StringComparison.Ordinal);
            if (!containsAudioApi)
            {
                continue;
            }

            if (!filePath.StartsWith(allowedDirectory, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(Path.GetRelativePath(ArchitectureTestSupport.FindRepositoryRoot(), filePath));
            }
        }

        Assert.True(
            violations.Count == 0,
            "AudioRecord/AudioTrack допускаются только в Platform.Android/Audio (порты IAudioCapture/IAudioPlayback): " +
            ArchitectureTestSupport.FormatList(violations));
    }

    [Fact]
    public void Manifest_объявляет_камеру_необязательной_для_pairing_UI()
    {
        var manifest = LoadManifest();

        var cameraFeature = manifest.Descendants()
            .Where(element => element.Name.LocalName == "uses-feature")
            .SingleOrDefault(element =>
                string.Equals(
                    (string?)element.Attribute(AndroidNamespace + "name"),
                    "android.hardware.camera.any",
                    StringComparison.Ordinal));

        Assert.NotNull(cameraFeature);
        Assert.Equal("false", (string?)cameraFeature!.Attribute(AndroidNamespace + "required"));

        // CAMERA — единственное camera-related разрешение: никакие audio/capture permissions не нужны.
        var permissions = ReadPermissions(manifest);
        Assert.Contains("android.permission.CAMERA", permissions, StringComparer.Ordinal);
    }

    [Fact]
    public void Manifest_запрещает_backup_пользовательских_данных()
    {
        var manifest = LoadManifest();
        var application = manifest.Root?.Elements().SingleOrDefault(element => element.Name.LocalName == "application");

        Assert.NotNull(application);
        Assert.Equal("false", (string?)application!.Attribute(AndroidNamespace + "allowBackup"));
    }

    private static XDocument LoadManifest() =>
        XDocument.Load(ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.App/AndroidManifest.xml"));

    private static List<string> ReadPermissions(XDocument manifest) =>
        manifest.Descendants()
            .Where(element => element.Name.LocalName == "uses-permission")
            .Select(element => (string?)element.Attribute(AndroidNamespace + "name"))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!)
            .ToList();
}
