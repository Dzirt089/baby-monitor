using System.Xml.Linq;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Гигиена AndroidManifest: минимальные разрешения для discovery (002) и отсутствие microphone
/// permissions (Parent не должен получать RECORD_AUDIO «на всякий случай»).
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
    ];

    private static readonly string[] ForbiddenPermissions =
    [
        "android.permission.RECORD_AUDIO",
        "android.permission.CAPTURE_AUDIO_OUTPUT",
        "android.permission.MODIFY_AUDIO_SETTINGS",
    ];

    [Fact]
    public void Manifest_содержит_только_ожидаемые_discovery_permissions()
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
    public void Foreground_service_объявляет_роли_microphone_и_mediaPlayback()
    {
        var source = File.ReadAllText(
            ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Platform.Android/ForegroundServices/MonitoringForegroundService.cs"));

        Assert.Contains("TypeMicrophone", source, StringComparison.Ordinal);
        Assert.Contains("TypeMediaPlayback", source, StringComparison.Ordinal);
        Assert.Contains("Exported = false", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RECORD_AUDIO", source, StringComparison.Ordinal);
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
