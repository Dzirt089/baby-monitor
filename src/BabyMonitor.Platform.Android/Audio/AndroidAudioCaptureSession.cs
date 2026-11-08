using Android.Media;
using Android.Util;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Results;
using AppAudioFormat = BabyMonitor.Application.Abstractions.Audio.AudioFormat;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Активная сессия захвата Child: фоновая задача блокирующе читает <c>AudioRecord</c> кадрами
/// 20 мс и складывает их в bounded-буфер; <see cref="ReadAsync"/> отдаёт не больше одного кадра.
/// </summary>
/// <remarks>
/// Чтение выполняется на фоновом потоке (не UI thread). При перегрузке потребителя старые кадры
/// отбрасываются, поэтому задержка не растёт безгранично. Ожидаемые завершения: <see cref="ReadAsync"/>
/// возвращает 0 после <see cref="StopAsync"/> (поток данных завершён); ошибка устройства поднимается
/// как <see cref="AndroidAudioAdapterException"/> с кодом из <see cref="AudioErrorCodes"/>; отмена —
/// <see cref="OperationCanceledException"/>; после <see cref="DisposeAsync"/> — 
/// <see cref="ObjectDisposedException"/>. PCM не пишется на диск и не логируется.
/// </remarks>
internal sealed class AndroidAudioCaptureSession : IAudioCaptureSession
{
    private const string LogTag = "BabyMonitor.Audio";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly AudioRecord _record;
    private readonly AppAudioFormat _format;
    private readonly BoundedAudioFrameBuffer _buffer;
    private readonly CancellationTokenSource _stopCancellation = new();
    private readonly object _stopGate = new();
    private readonly byte[] _readBuffer;
    private Task? _readLoop;
    private Task? _stopTask;
    private Error? _fault;
    private int _disposed;

    internal AndroidAudioCaptureSession(AudioRecord record, AppAudioFormat format, int capacityFrames)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(format);
        _record = record;
        _format = format;
        _buffer = new BoundedAudioFrameBuffer(capacityFrames, format.Frame20MsBytes);
        _readBuffer = new byte[format.Frame20MsBytes];
    }

    /// <inheritdoc />
    public AppAudioFormat Format => _format;

    /// <summary>
    /// Запускает запись и фоновую задачу чтения. Микрофон уже открыт адаптером; вызывается один раз.
    /// </summary>
    /// <returns>Типизированная ошибка запуска либо <see langword="null"/> при успехе.</returns>
    internal Error? TryStart()
    {
        try
        {
            _record.StartRecording();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioRecord.StartRecording failed: " + exception.GetType().Name);
            return new Error(
                AudioErrorCodes.CaptureUnavailable,
                "Не удалось включить запись микрофона.");
        }

        if (_record.RecordingState != RecordState.Recording)
        {
            return new Error(
                AudioErrorCodes.CaptureUnavailable,
                "Микрофон не перешёл в состояние записи.");
        }

        _readLoop = Task.Run(ReadLoopAsync);
        return null;
    }

    /// <inheritdoc />
    public async ValueTask<int> ReadAsync(Memory<byte> destination, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        if (destination.Length < _format.Frame20MsBytes)
        {
            throw new ArgumentException(
                "Буфер меньше одного кадра 20 мс.",
                nameof(destination));
        }

        var lease = await _buffer.TakeAsync(cancellationToken).ConfigureAwait(false);
        if (lease is null)
        {
            ThrowIfFaulted();
            return 0;
        }

        try
        {
            var frame = lease.Value;
            frame.CopyTo(destination.Span);
            return frame.Length;
        }
        finally
        {
            lease.Value.Release();
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        // Идемпотентность: повторный Stop/Stop после Dispose ждёт уже запущенную очистку.
        GetStopTask().WaitAsync(cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await GetStopTask().ConfigureAwait(false);
    }

    private Task GetStopTask()
    {
        lock (_stopGate)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        await _stopCancellation.CancelAsync().ConfigureAwait(false);

        // Прерываем блокирующий нативный read, чтобы фоновая задача завершилась в пределах StopTimeout.
        try
        {
            if (_record.RecordingState == RecordState.Recording)
            {
                _record.Stop();
            }
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioRecord.Stop failed: " + exception.GetType().Name);
        }

        if (_readLoop is not null)
        {
            try
            {
                await _readLoop.WaitAsync(StopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Warn(LogTag, "AudioRecord read loop did not stop in time.");
            }
        }

        _buffer.Complete();

        try
        {
            _record.Release();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioRecord release failed: " + exception.GetType().Name);
        }
        finally
        {
            _record.Dispose();
            _stopCancellation.Dispose();
        }
    }

    private async Task ReadLoopAsync()
    {
        var cancellationToken = _stopCancellation.Token;
        var frameBytes = _format.Frame20MsBytes;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var offset = 0;
                var readFailed = false;
                while (offset < frameBytes)
                {
                    var read = _record.Read(_readBuffer, offset, frameBytes - offset);
                    if (read < 0)
                    {
                        if (!cancellationToken.IsCancellationRequested)
                        {
                            SetFault(read);
                        }

                        readFailed = true;
                        break;
                    }

                    if (read == 0)
                    {
                        // Блокирующее чтение может вернуть 0 при прерывании — кадр добираем,
                        // но не крутимся, если сессию уже останавливают.
                        if (cancellationToken.IsCancellationRequested)
                        {
                            readFailed = true;
                            break;
                        }

                        continue;
                    }

                    offset += read;
                }

                if (readFailed || cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var enqueued = await _buffer
                    .EnqueueAsync(_readBuffer.AsMemory(0, frameBytes), cancellationToken)
                    .ConfigureAwait(false);
                if (!enqueued)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Штатная остановка сессии.
        }
        catch (Exception exception)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                Log.Warn(LogTag, "AudioRecord read loop failed: " + exception.GetType().Name);
                SetFault(-1);
            }
        }
        finally
        {
            _buffer.Complete();
        }
    }

    private void SetFault(int nativeResult)
    {
        var error = new Error(
            AudioErrorCodes.AdapterFailed,
            "Захват звука прерван ошибкой устройства.");
        if (Interlocked.CompareExchange(ref _fault, error, null) is not null)
        {
            return;
        }

        Log.Warn(LogTag, "AudioRecord read failed: result=" + nativeResult + " error=" + error.Code);
        _buffer.Complete();
    }

    private void ThrowIfFaulted()
    {
        var fault = Volatile.Read(ref _fault);
        if (fault is not null)
        {
            throw new AndroidAudioAdapterException(
                fault,
                "Захват звука прерван ошибкой устройства.");
        }
    }
}
