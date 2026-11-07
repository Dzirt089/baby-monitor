namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Гигиена Android security-адаптеров: секреты не логируются, trusted store не сериализует
/// private key/token, Keystore-adapter не экспортирует private key.
/// </summary>
public sealed class SecurityStorageTests
{
    private static readonly string SecurityDirectory =
        ArchitectureTestSupport.GetPathFromRoot("src/BabyMonitor.Platform.Android/Security");

    [Fact]
    public void Android_security_адаптеры_не_логируют_данные()
    {
        foreach (var file in Directory.GetFiles(SecurityDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var code = ReadCodeLines(file);
            Assert.DoesNotContain("Log.", code, StringComparison.Ordinal);
            Assert.DoesNotContain("Console.", code, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Trusted_store_сериализует_только_публичные_поля()
    {
        var source = ReadCodeLines(Path.Combine(SecurityDirectory, "AndroidTrustedDeviceStore.cs"));

        Assert.Contains("SubjectPublicKeyInfo", source, StringComparison.Ordinal);
        Assert.Contains("FingerprintSha256", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PrivateKey", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PairingToken", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keystore_adapter_не_экспортирует_private_key()
    {
        var source = ReadCodeLines(Path.Combine(SecurityDirectory, "AndroidKeystorePairingKeyStore.cs"));

        Assert.Contains("AndroidKeyStore", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ExportPkcs8", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ExportParameters(true", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("PrivateKey.GetEncoded", source, StringComparison.Ordinal);
    }

    private static string ReadCodeLines(string path) =>
        string.Join(
            "\n",
            File.ReadAllLines(path).Where(line =>
            {
                var trimmed = line.TrimStart();
                return !trimmed.StartsWith("//", StringComparison.Ordinal)
                    && !trimmed.StartsWith("*", StringComparison.Ordinal)
                    && !trimmed.StartsWith("/*", StringComparison.Ordinal);
            }));
}
