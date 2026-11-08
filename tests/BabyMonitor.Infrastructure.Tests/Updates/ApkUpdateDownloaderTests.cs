using System.Net;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;
using BabyMonitor.Infrastructure.Updates;

namespace BabyMonitor.Infrastructure.Tests.Updates;

/// <summary>
/// Contract/security тесты ApkUpdateDownloader (009, ADR-0012): HTTPS-загрузка соседнего APK-asset-а,
/// потоковая запись во временный файл, инкрементальный SHA-256/размер, удаление temp при ошибках,
/// typed-ошибки, прогресс, отмена и очистка устаревших temp-файлов.
/// </summary>
public sealed class ApkUpdateDownloaderTests
{
    [Fact]
    public async Task Валидная_загрузка_сохраняет_проверенный_apk_и_экранирует_url()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(8192);
        const string assetName = "Baby Monitor+0.5.1.apk";
        var manifest = UpdateTestData.CreateManifest(apkBytes, assetName);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var progress = new ProgressCollector();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, progress, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var expectedPath = Path.Combine(cache.Path, assetName);
        Assert.Equal(expectedPath, result.Value.FilePath);
        Assert.Equal(manifest, result.Value.Manifest);
        Assert.True(File.Exists(expectedPath));
        Assert.Equal(apkBytes, await File.ReadAllBytesAsync(expectedPath));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(
            ApkUpdateDownloader.ReleaseDownloadBaseUrl + Uri.EscapeDataString(assetName),
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, request.RequestUri.Scheme);

        Assert.NotEmpty(progress.Values);
        Assert.All(progress.Values, value => Assert.InRange(value, 0.0, 1.0));
        Assert.Equal(1.0, progress.Values[^1]);
    }

    [Fact]
    public async Task Ответ_без_известной_длины_с_совпадающим_размером_успешен()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(2048);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(apkBytes)
            }));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(apkBytes, await File.ReadAllBytesAsync(result.Value.FilePath));
        Assert.Single(cache.GetFileNames());
    }

    [Fact]
    public async Task Hash_mismatch_даёт_hash_mismatch_и_удаляет_temp()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(4096);
        var otherBytes = UpdateTestData.CreateApkBytes(4096).Reverse().ToArray();
        var manifest = UpdateTestData.CreateManifest(apkBytes, sha256: UpdateTestData.Sha256Hex(otherBytes));
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.HashMismatch, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Неверный_размер_с_известной_длиной_даёт_download_failed()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(4096);
        var manifest = UpdateTestData.CreateManifest(apkBytes, sizeBytes: apkBytes.Length - 1);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Превышение_размера_в_потоке_даёт_download_failed()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(4096);
        var manifest = UpdateTestData.CreateManifest(apkBytes, sizeBytes: apkBytes.Length - 1);
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(apkBytes)
            }));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Недостаточный_размер_в_потоке_даёт_download_failed()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(4096);
        var manifest = UpdateTestData.CreateManifest(apkBytes, sizeBytes: apkBytes.Length + 1);
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(apkBytes)
            }));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task HTTP_ошибка_даёт_download_failed(HttpStatusCode statusCode)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Сетевое_исключение_даёт_download_failed()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("network down")));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Небезопасное_имя_апк_отклоняется_без_сетевого_запроса()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(512);
        var manifest = UpdateTestData.CreateManifest(apkBytes, "../evil.apk");
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(cache.GetFileNames());
    }

    [Theory]
    [InlineData("not-a-hash", 1024L)]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef", 0L)]
    public async Task Некорректные_поля_манифеста_отклоняются_без_сети(string sha256, long sizeBytes)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(512);
        var manifest = new AppUpdateManifest(
            1,
            UpdateTestData.PackageId,
            "0.5.1",
            501,
            "BabyMonitor-0.5.1.apk",
            sha256,
            sizeBytes,
            null,
            null);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task Отмена_до_загрузки_бросает_OCE_и_не_создаёт_файлов()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await downloader.DownloadAsync(manifest, null, cancellation.Token));

        Assert.Equal(0, handler.CallCount);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Отмена_во_время_загрузки_удаляет_temp_файл()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(4096);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var stream = new OneChunkThenBlockStream();
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new RawStreamContent(stream)
            }));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);
        using var cancellation = new CancellationTokenSource();
        var progress = new CancellingProgress(cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await downloader.DownloadAsync(manifest, progress, cancellation.Token));

        Assert.NotEmpty(progress.Values);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Кэш_создаётся_если_каталога_нет()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var temp = new TempCacheDirectory();
        var cachePath = Path.Combine(temp.Path, "nested", "cache");
        var downloader = new ApkUpdateDownloader(httpClient, cachePath);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.True(Directory.Exists(cachePath));
        Assert.True(File.Exists(result.Value.FilePath));
    }

    [Fact]
    public async Task Устаревшие_temp_файлы_удаляются_а_свежие_остаются()
    {
        var now = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var oldTempPath = Path.Combine(cache.Path, "babymonitor-update-old.apk.tmp");
        var recentTempPath = Path.Combine(cache.Path, "babymonitor-update-recent.apk.tmp");
        await File.WriteAllBytesAsync(oldTempPath, [1, 2, 3]);
        await File.WriteAllBytesAsync(recentTempPath, [4, 5, 6]);
        File.SetLastWriteTimeUtc(oldTempPath, now.UtcDateTime.AddHours(-48));
        File.SetLastWriteTimeUtc(recentTempPath, now.UtcDateTime.AddHours(-1));
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path, new FixedUpdatesTimeProvider(now));

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.False(File.Exists(oldTempPath), "Устаревший temp-файл должен быть удалён.");
        Assert.True(File.Exists(recentTempPath), "Свежий temp-файл (потенциально активная загрузка) не удаляется.");
        Assert.True(File.Exists(Path.Combine(cache.Path, manifest.ApkAssetName)));
    }

    [Fact]
    public async Task Размер_apk_больше_максимума_отклоняется_без_запроса()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(512);
        var manifest = UpdateTestData.CreateManifest(apkBytes, sizeBytes: UpdateLimits.MaxApkSizeBytes + 1);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
        Assert.Equal(0, handler.CallCount);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Слишком_длинное_имя_апк_отклоняется_без_запроса()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(512);
        var longName = new string('a', UpdateLimits.MaxApkAssetNameLength) + ".apk";
        var manifest = UpdateTestData.CreateManifest(apkBytes, longName);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
        Assert.Equal(0, handler.CallCount);
    }

    [Theory]
    [InlineData("http://github.com/Dzirt089/baby-monitor/releases/latest/download/BabyMonitor-0.5.1.apk")]
    [InlineData("https://evil.example/BabyMonitor-0.5.1.apk")]
    [InlineData("https://github.com.evil.example/BabyMonitor-0.5.1.apk")]
    public async Task Финальный_redirect_на_недопустимый_адрес_даёт_download_failed(string finalUri)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = UpdateTestData.BytesResponse(apkBytes);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Финальный_redirect_на_githubusercontent_допускается()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = UpdateTestData.BytesResponse(apkBytes);
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://release-assets.githubusercontent.com/github-production-release-asset/BabyMonitor-0.5.1.apk");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(apkBytes, await File.ReadAllBytesAsync(result.Value.FilePath));
    }

    [Fact]
    public async Task Таймаут_загрузки_по_TimeProvider_даёт_download_failed()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(1024);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path, new ImmediateTimerTimeProvider());

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.DownloadFailed, result.Error!.Code);
        Assert.Empty(cache.GetFileNames());
    }

    [Fact]
    public async Task Повторная_загрузка_перезаписывает_существующий_файл()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(2048);
        var manifest = UpdateTestData.CreateManifest(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.BytesResponse(apkBytes)));
        using var httpClient = new HttpClient(handler);
        using var cache = new TempCacheDirectory();
        var finalPath = Path.Combine(cache.Path, manifest.ApkAssetName);
        await File.WriteAllBytesAsync(finalPath, [9, 9, 9]);
        var downloader = new ApkUpdateDownloader(httpClient, cache.Path);

        var result = await downloader.DownloadAsync(manifest, null, CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(apkBytes, await File.ReadAllBytesAsync(finalPath));
        Assert.Single(cache.GetFileNames());
    }
}
