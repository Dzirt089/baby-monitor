using System.Security.Cryptography;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;

namespace BabyMonitor.Infrastructure.Updates;

/// <summary>
/// Загрузчик APK-пакета обновления (009, ADR-0012): HTTPS GET соседнего asset-а latest release,
/// потоковая запись во временный файл в кэше, инкрементальный SHA-256 и проверка размера. При любой
/// ошибке/несовпадении временный файл удаляется; во внутренний кэш попадает только проверенный APK.
/// Токены/секреты не используются и не логируются.
/// </summary>
public sealed class ApkUpdateDownloader : IUpdatePackageDownloader
{
    /// <summary>Базовый URL asset-ов последнего релиза (APK — соседний с манифестом; только HTTPS).</summary>
    public const string ReleaseDownloadBaseUrl = "https://github.com/Dzirt089/baby-monitor/releases/latest/download/";

    private const string TempFilePrefix = "babymonitor-update-";
    private const string TempFileSuffix = ".apk.tmp";
    private const int BufferBytes = 81920;

    private static readonly TimeSpan StaleTempFileAge = TimeSpan.FromHours(24);
    private static readonly Uri ReleaseDownloadBaseUri = new(ReleaseDownloadBaseUrl, UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly string _cacheDirectory;
    private readonly TimeProvider _timeProvider;

    /// <summary>Создаёт загрузчик APK в кэш.</summary>
    /// <param name="httpClient">Инжектированный HttpClient (владелец — composition root; адаптер не освобождает).</param>
    /// <param name="cacheDirectory">Каталог внутреннего кэша для APK (создаётся при необходимости).</param>
    /// <param name="timeProvider">Источник времени для очистки устаревших temp-файлов; по умолчанию system.</param>
    /// <exception cref="ArgumentNullException"><paramref name="httpClient"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Каталог кэша пуст или некорректен.</exception>
    /// <exception cref="InvalidOperationException">Базовый URL ассетов не является HTTPS.</exception>
    public ApkUpdateDownloader(HttpClient httpClient, string cacheDirectory, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        if (!string.Equals(ReleaseDownloadBaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("APK-ассеты обновления должны запрашиваться только по HTTPS.");
        }

        _httpClient = httpClient;
        _cacheDirectory = Path.GetFullPath(cacheDirectory);
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<Result<DownloadedUpdatePackage>> DownloadAsync(
        AppUpdateManifest manifest,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        cancellationToken.ThrowIfCancellationRequested();

        // Защитная перепроверка полей (манифест мог прийти не от этого source-адаптера).
        if (manifest.SizeBytes <= 0
            || manifest.SizeBytes > UpdateLimits.MaxApkSizeBytes
            || !UpdateLimits.IsValidSha256(manifest.Sha256)
            || !UpdateLimits.IsSafeApkAssetName(manifest.ApkAssetName))
        {
            return Result<DownloadedUpdatePackage>.Failure(InvalidManifestError());
        }

        Uri assetUri;
        string tempPath;
        string finalPath;
        try
        {
            // LOW: построение URI/пути из недоверенного имени ассета — в try; любая ошибка = invalid_manifest.
            assetUri = BuildAssetUri(manifest.ApkAssetName);
            tempPath = Path.Combine(
                _cacheDirectory,
                TempFilePrefix + Guid.NewGuid().ToString("N") + TempFileSuffix);
            finalPath = Path.Combine(_cacheDirectory, manifest.ApkAssetName);
        }
        catch (Exception exception) when (exception is ArgumentException or UriFormatException or NotSupportedException or PathTooLongException)
        {
            return Result<DownloadedUpdatePackage>.Failure(InvalidManifestError());
        }

        try
        {
            Directory.CreateDirectory(_cacheDirectory);
        }
        catch (IOException)
        {
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }
        catch (UnauthorizedAccessException)
        {
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }

        TryCleanupStaleTempFiles();

        // LOW: HttpClient.Timeout не ограничивает чтение уже полученного потока — общий bounded timeout
        // на всю загрузку (через TimeProvider, как и очистка temp); caller cancellation сохраняется.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var timeoutTimer = _timeProvider.CreateTimer(
            static state => UpdateLimits.CancelOnTimeout((CancellationTokenSource)state!),
            timeoutCts,
            UpdateLimits.DownloadTimeout,
            Timeout.InfiniteTimeSpan);
        var operationToken = timeoutCts.Token;

        var completed = false;
        try
        {
            using var response = await _httpClient
                .GetAsync(assetUri, HttpCompletionOption.ResponseHeadersRead, operationToken)
                .ConfigureAwait(false);

            // M3: финальный адрес после redirect обязан быть HTTPS на разрешённом GitHub/asset-CDN хосте.
            if (!UpdateLimits.IsAllowedFinalUri(response.RequestMessage?.RequestUri))
            {
                return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
            }

            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength is { } contentLength
                && contentLength != manifest.SizeBytes)
            {
                return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
            }

            await using var source = await response.Content
                .ReadAsStreamAsync(operationToken)
                .ConfigureAwait(false);
            await using (var destination = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                BufferBytes,
                useAsync: true))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferBytes];
                long totalBytes = 0;
                var maxDownloadBytes = Math.Min(manifest.SizeBytes, UpdateLimits.MaxApkSizeBytes);
                int read;
                while ((read = await source.ReadAsync(buffer, operationToken).ConfigureAwait(false)) > 0)
                {
                    totalBytes += read;
                    if (totalBytes > maxDownloadBytes)
                    {
                        // Размер уже превысил манифест: не продолжаем писать недоверенные данные.
                        return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), operationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    ReportProgress(progress, totalBytes, manifest.SizeBytes);
                }

                await destination.FlushAsync(operationToken).ConfigureAwait(false);
                if (totalBytes != manifest.SizeBytes)
                {
                    return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
                }

                var actualSha256 = Convert.ToHexString(hash.GetHashAndReset());
                if (!string.Equals(actualSha256, manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return Result<DownloadedUpdatePackage>.Failure(HashMismatchError());
                }
            }

            File.Move(tempPath, finalPath, overwrite: true);
            completed = true;
            progress?.Report(1.0);
            return Result<DownloadedUpdatePackage>.Success(new DownloadedUpdatePackage(manifest, finalPath));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Таймаут потоковой загрузки (не отмена вызывающего): typed download_failed, temp удалит finally.
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException)
        {
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }
        catch (IOException)
        {
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }
        catch (UnauthorizedAccessException)
        {
            return Result<DownloadedUpdatePackage>.Failure(DownloadFailedError());
        }
        finally
        {
            if (!completed)
            {
                TryDeleteFile(tempPath);
            }
        }
    }

    private static Uri BuildAssetUri(string apkAssetName)
    {
        var escapedName = Uri.EscapeDataString(apkAssetName);
        var uri = new Uri(ReleaseDownloadBaseUrl + escapedName, UriKind.Absolute);
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("APK-ассет обновления должен запрашиваться только по HTTPS.");
        }

        return uri;
    }

    private void TryCleanupStaleTempFiles()
    {
        var cutoffUtc = (_timeProvider.GetUtcNow() - StaleTempFileAge).UtcDateTime;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_cacheDirectory, TempFilePrefix + "*" + TempFileSuffix))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(path) < cutoffUtc)
                    {
                        File.Delete(path);
                    }
                }
                catch (IOException)
                {
                    // Файл занят параллельной загрузкой/удалён конкурентно: очистка best-effort.
                }
                catch (UnauthorizedAccessException)
                {
                    // Нет прав на удаление отдельного файла: очистка best-effort.
                }
            }
        }
        catch (IOException)
        {
            // Каталог кэша недоступен для перечисления: очистка best-effort, загрузка продолжается.
        }
        catch (UnauthorizedAccessException)
        {
            // Нет прав на перечисление: очистка best-effort, загрузка продолжается.
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Файл занят другим процессом/уже удалён: повторная уборка не критична.
        }
        catch (UnauthorizedAccessException)
        {
            // Нет прав на удаление: повторная уборка не критична.
        }
    }

    private static void ReportProgress(IProgress<double>? progress, long bytesDownloaded, long totalBytes)
    {
        if (progress is null || totalBytes <= 0)
        {
            return;
        }

        progress.Report(Math.Min(1.0, (double)bytesDownloaded / totalBytes));
    }

    private static Error DownloadFailedError() => new(
        AppUpdateErrorCodes.DownloadFailed,
        "Не удалось скачать обновление.");

    private static Error HashMismatchError() => new(
        AppUpdateErrorCodes.HashMismatch,
        "Скачанный APK повреждён: SHA-256 не совпадает с манифестом.");

    private static Error InvalidManifestError() => new(
        AppUpdateErrorCodes.InvalidManifest,
        "Манифест обновления некорректен.");
}
