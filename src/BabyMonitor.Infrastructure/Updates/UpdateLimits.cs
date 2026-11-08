namespace BabyMonitor.Infrastructure.Updates;

/// <summary>
/// Общие лимиты и политика безопасности канала автообновления (009, ADR-0012): максимальный размер
/// APK, ограничение длины имени ассета, таймаут потоковой загрузки и allowlist финальных
/// HTTPS-хостов после redirect-цепочки. Используется source-ом и downloader-ом одинаково.
/// </summary>
public static class UpdateLimits
{
    /// <summary>Максимальный допустимый размер APK-обновления (200 MiB).</summary>
    public const long MaxApkSizeBytes = 200L * 1024 * 1024;

    /// <summary>Максимальная длина имени APK-ассета (защита от патологических/слишком длинных путей).</summary>
    internal const int MaxApkAssetNameLength = 200;

    /// <summary>Длина hex-представления SHA-256.</summary>
    internal const int Sha256HexLength = 64;

    /// <summary>
    /// Максимальная длительность потоковой загрузки APK: HttpClient.Timeout не действует на чтение
    /// уже полученного потока, поэтому нужен собственный bounded timeout.
    /// </summary>
    internal static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Точный allowlist хостов, допустимых для финального ответа после redirect (без suffix-эвристик):
    /// GitHub Releases и его asset-CDN. Схема обязана быть HTTPS.
    /// </summary>
    private static readonly string[] AllowedFinalHosts =
    [
        "github.com",
        "objects.githubusercontent.com",
        "release-assets.githubusercontent.com"
    ];

    /// <summary>
    /// Проверяет финальный URI ответа после redirect-цепочки: только HTTPS и точный allowlist
    /// GitHub-хостов. Защита от downgrade/подмены redirect на cleartext или чужой хост.
    /// </summary>
    internal static bool IsAllowedFinalUri(Uri? finalUri) =>
        finalUri is not null
        && string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
        && AllowedFinalHosts.Contains(finalUri.Host, StringComparer.OrdinalIgnoreCase);

    /// <summary>SHA-256 в hex (допускается верхний регистр; наружу манифест отдаётся lowercase).</summary>
    internal static bool IsValidSha256(string? value)
    {
        if (value is null || value.Length != Sha256HexLength)
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Имя APK-ассета: только имя файла без пути/traversal, разумной длины и с расширением .apk.</summary>
    internal static bool IsSafeApkAssetName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > MaxApkAssetNameLength
            || value.Length <= ".apk".Length
            || value.Contains('/', StringComparison.Ordinal)
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || !value.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Идемпотентная отмена по таймеру, безопасная для уже освобождённого CTS.</summary>
    internal static void CancelOnTimeout(CancellationTokenSource timeoutCts)
    {
        try
        {
            timeoutCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Операция уже завершена и CTS освобождён: таймер опоздал, отмена не нужна.
        }
    }
}
