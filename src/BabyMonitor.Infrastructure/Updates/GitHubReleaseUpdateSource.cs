using System.Globalization;
using System.Text.Json;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;

namespace BabyMonitor.Infrastructure.Updates;

/// <summary>
/// Источник обновлений на публичных GitHub Releases (009, ADR-0012): HTTPS GET манифеста
/// update-manifest.json из latest release, строгая схема v1 и типизированные ошибки. Токены/секреты
/// не используются и не логируются; HTTP-клиент инжектируется composition root-ом (адаптер им не владеет).
/// </summary>
public sealed class GitHubReleaseUpdateSource : IAppUpdateSource
{
    /// <summary>Канонический URL манифеста последнего релиза (только HTTPS, без API/токена).</summary>
    public const string ManifestUrl = "https://github.com/Dzirt089/baby-monitor/releases/latest/download/update-manifest.json";

    /// <summary>Версия схемы манифеста, поддерживаемая клиентом.</summary>
    internal const int SupportedSchemaVersion = 1;

    /// <summary>Таймаут HTTPS-запроса манифеста (через таймер <see cref="TimeProvider"/>).</summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private const int MaxManifestBytes = 64 * 1024;
    private const int ReadBufferBytes = 4096;

    private static readonly Uri ManifestUri = new(ManifestUrl, UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;

    /// <summary>Создаёт источник манифеста обновлений.</summary>
    /// <param name="httpClient">Инжектированный HttpClient (владелец — composition root; адаптер не освобождает).</param>
    /// <param name="timeProvider">Источник времени для таймаута; по умолчанию <see cref="TimeProvider.System"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">URL манифеста не является HTTPS.</exception>
    public GitHubReleaseUpdateSource(HttpClient httpClient, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        if (!string.Equals(ManifestUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Манифест обновления должен запрашиваться только по HTTPS.");
        }

        _httpClient = httpClient;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<AppUpdateManifest>> GetLatestManifestAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timeoutTimer = _timeProvider.CreateTimer(
            static state => UpdateLimits.CancelOnTimeout((CancellationTokenSource)state!),
            timeoutCts,
            RequestTimeout,
            Timeout.InfiniteTimeSpan);

        try
        {
            using var response = await _httpClient
                .GetAsync(ManifestUri, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token)
                .ConfigureAwait(false);

            // M3: финальный адрес после redirect-цепочки обязан быть HTTPS GitHub/asset-CDN;
            // иначе — cleartext/чужой хост/downgrade, typed network-отказ.
            if (!UpdateLimits.IsAllowedFinalUri(response.RequestMessage?.RequestUri))
            {
                return Result<AppUpdateManifest>.Failure(NetworkError());
            }

            response.EnsureSuccessStatusCode();

            var manifestBytesResult = await ReadManifestBytesAsync(response, timeoutCts.Token).ConfigureAwait(false);
            if (!manifestBytesResult.IsSuccess)
            {
                return Result<AppUpdateManifest>.Failure(manifestBytesResult.Error!);
            }

            return ParseManifest(manifestBytesResult.Value);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут запроса (не отмена вызывающего): typed network-ошибка без деталей.
            return Result<AppUpdateManifest>.Failure(NetworkError());
        }
        catch (HttpRequestException)
        {
            return Result<AppUpdateManifest>.Failure(NetworkError());
        }
        catch (IOException)
        {
            return Result<AppUpdateManifest>.Failure(NetworkError());
        }
    }

    private static async Task<Result<byte[]>> ReadManifestBytesAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var stream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        var buffer = new byte[ReadBufferBytes];
        using var memory = new MemoryStream();
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > MaxManifestBytes)
            {
                // Ограничение размера: недоверенный/повреждённый ответ не раздувает память.
                return Result<byte[]>.Failure(InvalidManifestError());
            }
        }

        return Result<byte[]>.Success(memory.ToArray());
    }

    private static Result<AppUpdateManifest> ParseManifest(byte[] manifestBytes)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(manifestBytes);
        }
        catch (JsonException)
        {
            return Result<AppUpdateManifest>.Failure(InvalidManifestError());
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetRequiredInt32(root, "schemaVersion", out var schemaVersion))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (schemaVersion != SupportedSchemaVersion)
            {
                return Result<AppUpdateManifest>.Failure(UnsupportedSchemaError());
            }

            if (!TryGetRequiredNonEmptyString(root, "packageId", out var packageId)
                || !TryGetRequiredNonEmptyString(root, "versionName", out var versionName)
                || !TryGetRequiredInt32(root, "versionCode", out var versionCode))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetRequiredNonEmptyString(root, "apkAssetName", out var apkAssetName)
                || !UpdateLimits.IsSafeApkAssetName(apkAssetName))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetRequiredNonEmptyString(root, "sha256", out var sha256)
                || !UpdateLimits.IsValidSha256(sha256))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetRequiredInt64(root, "sizeBytes", out var sizeBytes)
                || sizeBytes <= 0
                || sizeBytes > UpdateLimits.MaxApkSizeBytes)
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetOptionalString(root, "notes", out var notes))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            if (!TryGetOptionalPublishedAt(root, out var publishedAt))
            {
                return Result<AppUpdateManifest>.Failure(InvalidManifestError());
            }

            return Result<AppUpdateManifest>.Success(new AppUpdateManifest(
                schemaVersion,
                packageId,
                versionName,
                versionCode,
                apkAssetName,
                sha256.ToLowerInvariant(),
                sizeBytes,
                notes,
                publishedAt));
        }
    }

    private static bool TryGetRequiredNonEmptyString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        value = text;
        return true;
    }

    private static bool TryGetRequiredInt32(JsonElement root, string propertyName, out int value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    private static bool TryGetRequiredInt64(JsonElement root, string propertyName, out long value)
    {
        value = 0;
        return root.TryGetProperty(propertyName, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out value);
    }

    private static bool TryGetOptionalString(JsonElement root, string propertyName, out string? value)
    {
        value = null;
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return true;
    }

    private static bool TryGetOptionalPublishedAt(JsonElement root, out DateTimeOffset? publishedAt)
    {
        publishedAt = null;
        if (!root.TryGetProperty("publishedAt", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = element.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }

        publishedAt = parsed;
        return true;
    }

    private static Error NetworkError() => new(
        AppUpdateErrorCodes.Network,
        "Не удалось получить данные об обновлении из сети.");

    private static Error InvalidManifestError() => new(
        AppUpdateErrorCodes.InvalidManifest,
        "Манифест обновления некорректен.");

    private static Error UnsupportedSchemaError() => new(
        AppUpdateErrorCodes.UnsupportedSchema,
        "Версия формата обновления не поддерживается.");
}
