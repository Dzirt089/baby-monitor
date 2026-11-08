using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabyMonitor.Application.Updates;

namespace BabyMonitor.Infrastructure.Tests.Updates;

/// <summary>Fake HttpMessageHandler: записывает запросы и отдаёт подготовленный ответ/исключение.</summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handler = handler;
    }

    public List<HttpRequestMessage> Requests { get; } = [];

    public int CallCount => Requests.Count;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = await _handler(request, cancellationToken).ConfigureAwait(false);

        // Как и реальный SocketsHttpHandler: финальный RequestUri ответа доступен для проверки
        // redirect-цепочки. Тест может подменить его явно (http/чужой хост) — тогда не перезаписываем.
        response.RequestMessage ??= request;
        return response;
    }
}

/// <summary>Тестовые данные манифеста/APK и построение JSON без секретов/PII.</summary>
internal static class UpdateTestData
{
    public const string PackageId = "io.github.dzirt089.babymonitor";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static AppUpdateManifest CreateManifest(
        byte[] apkBytes,
        string apkAssetName = "BabyMonitor-0.5.1.apk",
        int schemaVersion = 1,
        int versionCode = 501,
        string versionName = "0.5.1",
        string? sha256 = null,
        long? sizeBytes = null,
        string? notes = "test notes",
        DateTimeOffset? publishedAt = null) =>
        new(
            schemaVersion,
            PackageId,
            versionName,
            versionCode,
            apkAssetName,
            sha256 ?? Sha256Hex(apkBytes),
            sizeBytes ?? apkBytes.Length,
            notes,
            publishedAt);

    public static string CreateManifestJson(
        byte[] apkBytes,
        Action<Dictionary<string, object?>>? customize = null)
    {
        var manifest = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1,
            ["packageId"] = PackageId,
            ["versionName"] = "0.5.1",
            ["versionCode"] = 501,
            ["apkAssetName"] = "BabyMonitor-0.5.1.apk",
            ["sha256"] = Sha256Hex(apkBytes),
            ["sizeBytes"] = apkBytes.Length,
            ["notes"] = "test notes",
            ["publishedAt"] = "2026-11-08T12:00:00Z"
        };
        customize?.Invoke(manifest);
        return JsonSerializer.Serialize(manifest, JsonOptions);
    }

    public static byte[] CreateApkBytes(int length = 4096)
    {
        var bytes = new byte[length];
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)((index * 31) + 7);
        }

        return bytes;
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    public static HttpResponseMessage BytesResponse(byte[] bytes, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode)
        {
            Content = new ByteArrayContent(bytes)
        };
}

/// <summary>Временный каталог кэша с очисткой после теста.</summary>
internal sealed class TempCacheDirectory : IDisposable
{
    public TempCacheDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "baby-monitor-update-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string[] GetFileNames() =>
        Directory.Exists(Path)
            ? Directory.GetFiles(Path).Select(file => System.IO.Path.GetFileName(file)!).ToArray()
            : [];

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Временный каталог мог остаться занят файловой системой после теста: уборка best-effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Нет прав на удаление temp-каталога: уборка best-effort.
        }
    }
}

/// <summary>Управляемое время для cleanup/timeout-тестов.</summary>
internal sealed class FixedUpdatesTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    public FixedUpdatesTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;

    public void SetUtcNow(DateTimeOffset utcNow) => _utcNow = utcNow;
}

/// <summary>TimeProvider, у которого таймер срабатывает сразу (детерминированный timeout).</summary>
internal sealed class ImmediateTimerTimeProvider : TimeProvider
{
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        callback(state);
        return new NoopTimer();
    }
}

internal sealed class NoopTimer : ITimer
{
    public bool Change(TimeSpan dueTime, TimeSpan period) => true;

    public void Dispose()
    {
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

/// <summary>Сбор прогресса загрузки для assertions.</summary>
internal sealed class ProgressCollector : IProgress<double>
{
    public List<double> Values { get; } = [];

    public void Report(double value) => Values.Add(value);
}

/// <summary>Прогресс, который отменяет операцию после первого отчёта (mid-stream cancellation).</summary>
internal sealed class CancellingProgress : IProgress<double>
{
    private readonly CancellationTokenSource _cancellation;

    public CancellingProgress(CancellationTokenSource cancellation) => _cancellation = cancellation;

    public List<double> Values { get; } = [];

    public void Report(double value)
    {
        Values.Add(value);
        _cancellation.Cancel();
    }
}

/// <summary>HttpContent без известной длины: проверяет подсчёт размера по потоку.</summary>
internal sealed class UnknownLengthContent : HttpContent
{
    private readonly byte[] _bytes;

    public UnknownLengthContent(byte[] bytes) => _bytes = bytes;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        stream.Write(_bytes, 0, _bytes.Length);
        return Task.CompletedTask;
    }
}

/// <summary>HttpContent, отдающий поток напрямую (для mid-stream cancellation).</summary>
internal sealed class RawStreamContent : HttpContent
{
    private readonly Stream _stream;

    public RawStreamContent(Stream stream) => _stream = stream;

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
        throw new NotSupportedException("RawStreamContent используется только через CreateContentReadStreamAsync.");

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_stream);
}

/// <summary>Поток: один байт, затем блокировка до отмены (проверка cleanup temp-файла).</summary>
internal sealed class OneChunkThenBlockStream : Stream
{
    private int _reads;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_reads == 0)
        {
            _reads++;
            buffer.Span[0] = 42;
            return 1;
        }

        await Task.Delay(Timeout.Infinite, cancellationToken);
        return 0;
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
