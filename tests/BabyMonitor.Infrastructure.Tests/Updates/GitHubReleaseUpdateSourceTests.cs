using System.Net;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;
using BabyMonitor.Infrastructure.Updates;

namespace BabyMonitor.Infrastructure.Tests.Updates;

/// <summary>
/// Contract/security тесты GitHubReleaseUpdateSource (009, ADR-0012): HTTPS GET манифеста latest release,
/// строгая схема v1, безопасные поля, typed-ошибки (network/invalid_manifest/unsupported_schema),
/// отмена/таймаут и отсутствие cleartext.
/// </summary>
public sealed class GitHubReleaseUpdateSourceTests
{
    [Fact]
    public async Task Валидный_манифест_читается_HTTPS_GET_и_sha_нормализуется()
    {
        var apkBytes = UpdateTestData.CreateApkBytes();
        var json = UpdateTestData.CreateManifestJson(
            apkBytes,
            manifest => manifest["sha256"] = UpdateTestData.Sha256Hex(apkBytes).ToUpperInvariant());
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient, new FixedUpdatesTimeProvider(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)));

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        var manifest = result.Value;
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Equal(UpdateTestData.PackageId, manifest.PackageId);
        Assert.Equal("0.5.1", manifest.VersionName);
        Assert.Equal(501, manifest.VersionCode);
        Assert.Equal("BabyMonitor-0.5.1.apk", manifest.ApkAssetName);
        Assert.Equal(UpdateTestData.Sha256Hex(apkBytes), manifest.Sha256);
        Assert.Equal(apkBytes.Length, manifest.SizeBytes);
        Assert.Equal("test notes", manifest.Notes);
        Assert.Equal(new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero), manifest.PublishedAt);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(GitHubReleaseUpdateSource.ManifestUrl, request.RequestUri!.AbsoluteUri);
        Assert.Equal(Uri.UriSchemeHttps, request.RequestUri.Scheme);
    }

    [Fact]
    public async Task Манифест_без_notes_и_publishedAt_читается()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(128);
        var json = UpdateTestData.CreateManifestJson(
            apkBytes,
            manifest =>
            {
                manifest.Remove("notes");
                manifest.Remove("publishedAt");
            });
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Null(result.Value.Notes);
        Assert.Null(result.Value.PublishedAt);
    }

    [Fact]
    public async Task Malformed_json_даёт_invalid_manifest()
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse("{not-json")));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Theory]
    [InlineData("schemaVersion")]
    [InlineData("packageId")]
    [InlineData("versionName")]
    [InlineData("versionCode")]
    [InlineData("apkAssetName")]
    [InlineData("sha256")]
    [InlineData("sizeBytes")]
    public async Task Отсутствие_обязательного_поля_даёт_invalid_manifest(string propertyName)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest.Remove(propertyName));
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Theory]
    [InlineData("short")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("")]
    public async Task Неверный_формат_sha256_даёт_invalid_manifest(string sha256)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["sha256"] = sha256);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Theory]
    [InlineData("../evil.apk")]
    [InlineData("dir/app.apk")]
    [InlineData("app.txt")]
    [InlineData("app..apk")]
    [InlineData("   ")]
    public async Task Небезопасное_имя_apk_ассета_отклоняется(string apkAssetName)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["apkAssetName"] = apkAssetName);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Неподдерживаемая_schemaVersion_даёт_unsupported_schema(int schemaVersion)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["schemaVersion"] = schemaVersion);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.UnsupportedSchema, result.Error!.Code);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    public async Task HTTP_ошибка_даёт_network(HttpStatusCode statusCode)
    {
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Network, result.Error!.Code);
    }

    [Fact]
    public async Task Сетевое_исключение_даёт_network()
    {
        var handler = new FakeHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("network down")));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Network, result.Error!.Code);
    }

    [Fact]
    public async Task Неверный_publishedAt_даёт_invalid_manifest()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["publishedAt"] = "not-a-date");
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Fact]
    public async Task schemaVersion_строкой_даёт_invalid_manifest()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["schemaVersion"] = "1");
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Fact]
    public async Task Слишком_большой_ответ_отклоняется_как_invalid_manifest()
    {
        var hugeNotes = new string('x', 70 * 1024);
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["notes"] = hugeNotes);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Fact]
    public async Task Отмена_запроса_пробрасывается_как_OperationCanceledException()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);
        using var cancellation = new CancellationTokenSource();

        var requestTask = source.GetLatestManifestAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await requestTask.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Размер_apk_больше_максимума_даёт_invalid_manifest()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(
            apkBytes,
            manifest => manifest["sizeBytes"] = UpdateLimits.MaxApkSizeBytes + 1);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Fact]
    public async Task Слишком_длинное_имя_апк_ассета_отклоняется()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var longName = new string('a', UpdateLimits.MaxApkAssetNameLength) + ".apk";
        var json = UpdateTestData.CreateManifestJson(apkBytes, manifest => manifest["apkAssetName"] = longName);
        var handler = new FakeHttpMessageHandler((_, _) => Task.FromResult(UpdateTestData.JsonResponse(json)));
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.InvalidManifest, result.Error!.Code);
    }

    [Theory]
    [InlineData("http://github.com/Dzirt089/baby-monitor/releases/latest/download/update-manifest.json")]
    [InlineData("https://evil.example/update-manifest.json")]
    [InlineData("https://github.com.evil.example/update-manifest.json")]
    public async Task Финальный_redirect_на_недопустимый_адрес_даёт_network(string finalUri)
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = UpdateTestData.JsonResponse(json);
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalUri);
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Network, result.Error!.Code);
    }

    [Fact]
    public async Task Финальный_redirect_на_githubusercontent_допускается()
    {
        var apkBytes = UpdateTestData.CreateApkBytes(256);
        var json = UpdateTestData.CreateManifestJson(apkBytes);
        var handler = new FakeHttpMessageHandler((_, _) =>
        {
            var response = UpdateTestData.JsonResponse(json);
            response.RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://objects.githubusercontent.com/github-production-release-asset/update-manifest.json");
            return Task.FromResult(response);
        });
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient);

        var result = await source.GetLatestManifestAsync(CancellationToken.None);

        Assert.True(result.IsSuccess, result.Error?.Code);
        Assert.Equal(UpdateTestData.PackageId, result.Value.PackageId);
    }

    [Fact]
    public async Task Таймаут_запроса_по_TimeProvider_даёт_network()
    {
        var handler = new FakeHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var httpClient = new HttpClient(handler);
        var source = new GitHubReleaseUpdateSource(httpClient, new ImmediateTimerTimeProvider());

        var result = await source.GetLatestManifestAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(result.IsSuccess);
        Assert.Equal(AppUpdateErrorCodes.Network, result.Error!.Code);
    }
}
