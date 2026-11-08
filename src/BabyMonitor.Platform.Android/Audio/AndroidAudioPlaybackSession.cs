using System.Runtime.Versioning;
using Android.Media;
using Android.Util;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Results;
using AppAudioFormat = BabyMonitor.Application.Abstractions.Audio.AudioFormat;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Активная сессия воспроизведения Parent: bounded-очередь PCM-кадров и фоновая задача записи в
/// <c>AudioTrack</c>; audio focus удерживается на время сессии.
/// </summary>
/// <remarks>
/// <para>
/// Запись в <c>AudioTrack</c> выполняется на фоновом потоке (не UI thread) и не блокирует
/// <see cref="WriteAsync"/>: очередь ограничена, при перегрузке старые кадры отбрасываются, поэтому
/// задержка не растёт безгранично (AGENTS.md §7).
/// </para>
/// <para>
/// Audio focus: при LOSS/LOSS_TRANSIENT/LOSS_TRANSIENT_CAN_DUCK воспроизведение сразу
/// останавливается (<c>Pause</c>/<c>Flush</c>, звук не играет поверх звонка/другого приложения),
/// а <see cref="WriteAsync"/> поднимает типизированную ошибку
/// <see cref="AudioErrorCodes.PlaybackUnavailable"/>, чтобы вызывающий слой увидел отказ.
/// AudioTrack при этом не пересоздаётся (пересоздание в callback-потоке рискованно): при возврате
/// AUDIOFOCUS_GAIN сессия возобновляет воспроизведение (<c>Play</c>), поэтому потеря фокуса
/// считается восстановимой. Если возобновление или
/// запись в устройство не удались, сессия терминально завершается и последующие вызовы получают
/// <see cref="AndroidAudioAdapterException"/>; итоговая очистка — <see cref="StopAsync"/>/
/// <see cref="DisposeAsync"/> (идемпотентно). PCM не пишется на диск и не логируется.
/// </para>
/// </remarks>
internal sealed class AndroidAudioPlaybackSession : IAudioPlaybackSession
{
    private const string LogTag = "BabyMonitor.Audio";

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly AudioManager _audioManager;
    private readonly AudioAttributes _audioAttributes;
    private readonly AppAudioFormat _format;
    private readonly int _capacityFrames;
    private readonly BoundedAudioFrameBuffer _buffer;
    private readonly AudioFocusChangeListener _focusListener;
    private readonly CancellationTokenSource _stopCancellation = new();
    private readonly object _stopGate = new();
    private readonly object _trackGate = new();
    private Task? _writerLoop;
    private Task? _stopTask;
    private AudioTrack? _track;
    private AudioFocusRequestClass? _focusRequest;
    private Error? _focusFault;
    private Error? _terminalFault;
    private int _focusLost;
    private int _stopped;
    private int _disposed;

    internal AndroidAudioPlaybackSession(
        AudioManager audioManager,
        AudioAttributes audioAttributes,
        AppAudioFormat format,
        int capacityFrames)
    {
        ArgumentNullException.ThrowIfNull(audioManager);
        ArgumentNullException.ThrowIfNull(audioAttributes);
        ArgumentNullException.ThrowIfNull(format);
        _audioManager = audioManager;
        _audioAttributes = audioAttributes;
        _format = format;
        _capacityFrames = capacityFrames;
        _buffer = new BoundedAudioFrameBuffer(capacityFrames, format.Frame20MsBytes);
        _focusListener = new AudioFocusChangeListener(this);
    }

    /// <inheritdoc />
    public AppAudioFormat Format => _format;

    /// <summary>
    /// Создаёт <c>AudioTrack</c>, запрашивает audio focus, запускает воспроизведение и фоновую
    /// задачу записи. Вызывается один раз адаптером.
    /// </summary>
    /// <returns>Типизированная ошибка запуска либо <see langword="null"/> при успехе.</returns>
    internal Error? TryStart()
    {
        AudioTrack track;
        try
        {
            var minBufferSize = AudioTrack.GetMinBufferSize(
                _format.SampleRateHz,
                ChannelOut.Mono,
                Encoding.Pcm16bit);
            if (minBufferSize <= 0)
            {
                return new Error(
                    AudioErrorCodes.PlaybackUnavailable,
                    "Устройство вывода не поддерживает формат 48 kHz mono 16 bit.");
            }

            var bufferSizeBytes = Math.Max(minBufferSize, _format.Frame20MsBytes * _capacityFrames);

            // Binding помечает Build()/fluent-методы nullable; фактически Android возвращает
            // непустой объект или бросает исключение — проверяем явно.
            var audioFormatBuilder = new global::Android.Media.AudioFormat.Builder();
            audioFormatBuilder.SetEncoding(Encoding.Pcm16bit);
            audioFormatBuilder.SetSampleRate(_format.SampleRateHz);
            audioFormatBuilder.SetChannelMask(ChannelOut.Mono);
            var audioFormat = audioFormatBuilder.Build();
            if (audioFormat is null)
            {
                return new Error(
                    AudioErrorCodes.PlaybackUnavailable,
                    "Не удалось задать формат воспроизведения 48 kHz mono 16 bit.");
            }

            var trackBuilder = new AudioTrack.Builder();
            trackBuilder.SetAudioAttributes(_audioAttributes);
            trackBuilder.SetAudioFormat(audioFormat);
            trackBuilder.SetTransferMode(AudioTrackMode.Stream);
            trackBuilder.SetBufferSizeInBytes(bufferSizeBytes);
            var builtTrack = trackBuilder.Build();
            if (builtTrack is null)
            {
                return new Error(
                    AudioErrorCodes.PlaybackUnavailable,
                    "Не удалось открыть устройство вывода звука.");
            }

            track = builtTrack;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioTrack init failed: " + exception.GetType().Name);
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось открыть устройство вывода звука.");
        }

        if (track.State != AudioTrackState.Initialized)
        {
            ReleaseTrackInstance(track);
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Устройство вывода звука недоступно.");
        }

        _track = track;
        var focusError = RequestAudioFocus();
        if (focusError is not null)
        {
            ReleaseTrack();
            return focusError;
        }

        try
        {
            track.Play();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioTrack.Play failed: " + exception.GetType().Name);
            AbandonAudioFocus();
            ReleaseTrack();
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось запустить воспроизведение звука.");
        }

        _writerLoop = Task.Run(WriterLoopAsync);
        return null;
    }

    /// <inheritdoc />
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> pcm, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) == 1, this);
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfUnavailable();

        if (pcm.IsEmpty)
        {
            throw new ArgumentException("PCM-блок пуст.", nameof(pcm));
        }

        var remaining = pcm;
        while (!remaining.IsEmpty)
        {
            var length = Math.Min(remaining.Length, _buffer.FrameBytes);
            var enqueued = await _buffer
                .EnqueueAsync(remaining[..length], cancellationToken)
                .ConfigureAwait(false);
            if (!enqueued)
            {
                ThrowIfUnavailable();
                throw new ObjectDisposedException(
                    GetType().Name,
                    "Сессия воспроизведения остановлена.");
            }

            remaining = remaining[length..];
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
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        await _stopCancellation.CancelAsync().ConfigureAwait(false);

        // Останавливаем вывод до ожидания фоновой задачи: это разблокирует возможную нативную
        // запись в AudioTrack, чтобы Stop не зависел от состояния аудиоустройства.
        lock (_trackGate)
        {
            try
            {
                _track?.Pause();
                _track?.Flush();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "AudioTrack pause failed: " + exception.GetType().Name);
            }
        }

        if (_writerLoop is not null)
        {
            try
            {
                await _writerLoop.WaitAsync(StopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                Log.Warn(LogTag, "AudioTrack write loop did not stop in time.");
            }
        }

        _buffer.Complete();
        ReleaseTrack();
        AbandonAudioFocus();
        _focusListener.Dispose();
        _stopCancellation.Dispose();
    }

    private async Task WriterLoopAsync()
    {
        var cancellationToken = _stopCancellation.Token;
        try
        {
            while (true)
            {
                var lease = await _buffer.TakeAsync(cancellationToken).ConfigureAwait(false);
                if (lease is null)
                {
                    break;
                }

                try
                {
                    if (Volatile.Read(ref _focusLost) == 1)
                    {
                        // Пока focus потерян, кадры отбрасываются: после regain не играем устаревшее.
                        continue;
                    }

                    var track = _track;
                    if (track is null)
                    {
                        break;
                    }

                    WriteFrame(track, lease.Value);
                }
                finally
                {
                    lease.Value.Release();
                }

                if (Volatile.Read(ref _terminalFault) is not null || Volatile.Read(ref _stopped) == 1)
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
            Log.Warn(LogTag, "AudioTrack write loop failed: " + exception.GetType().Name);
            SetTerminalFault(new Error(
                AudioErrorCodes.AdapterFailed,
                "Воспроизведение остановлено ошибкой устройства."));
        }
        finally
        {
            _buffer.Complete();
        }
    }

    private void WriteFrame(AudioTrack track, in BoundedAudioFrameBuffer.FrameLease lease)
    {
        // Аудиоустройство Android не документировано как потокобезопасное: запись, пауза и
        // освобождение сериализуются _trackGate. Блокирующая запись ограничена размером буфера
        // AudioTrack, поэтому потеря focus/Stop не зависают.
        lock (_trackGate)
        {
            if (Volatile.Read(ref _focusLost) == 1)
            {
                return;
            }

            var offset = 0;
            while (offset < lease.Length)
            {
                var written = track.Write(lease.Buffer, offset, lease.Length - offset);
                if (written < 0)
                {
                    if (Volatile.Read(ref _focusLost) == 1 || Volatile.Read(ref _stopped) == 1)
                    {
                        return;
                    }

                    SetTerminalFault(new Error(
                        AudioErrorCodes.AdapterFailed,
                        "Воспроизведение остановлено ошибкой устройства."));
                    return;
                }

                if (written == 0)
                {
                    // Данные не приняты (пауза/потеря фокуса) — кадр отбрасывается.
                    return;
                }

                offset += written;
            }
        }
    }

    private Error? RequestAudioFocus()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            return RequestAudioFocusModern();
        }

        return RequestAudioFocusLegacy();
    }

    [SupportedOSPlatform("android26.0")]
    private Error? RequestAudioFocusModern()
    {
        AudioFocusRequestClass? request;
        try
        {
            var requestBuilder = new AudioFocusRequestClass.Builder(AudioFocus.Gain);
            requestBuilder.SetAudioAttributes(_audioAttributes);
            requestBuilder.SetOnAudioFocusChangeListener(_focusListener);
            // Duck не подходит радионяне: при ducking останавливаем воспроизведение целиком.
            requestBuilder.SetWillPauseWhenDucked(true);
            request = requestBuilder.Build();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Audio focus request build failed: " + exception.GetType().Name);
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось запросить аудиофокус для воспроизведения.");
        }

        if (request is null)
        {
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось запросить аудиофокус для воспроизведения.");
        }

        var result = _audioManager.RequestAudioFocus(request);
        if (result != AudioFocusRequest.Granted)
        {
            request.Dispose();
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Аудиофокус занят другим приложением: воспроизведение недоступно.");
        }

        _focusRequest = request;
        return null;
    }

    private Error? RequestAudioFocusLegacy()
    {
        // API 24-25: единственный доступный способ запроса фокуса; minSdk проекта — 24.
#pragma warning disable CA1422 // Obsolete audio focus API для API 24-25.
        var result = _audioManager.RequestAudioFocus(_focusListener, global::Android.Media.Stream.Music, AudioFocus.Gain);
#pragma warning restore CA1422
        if (result != AudioFocusRequest.Granted)
        {
            return new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Аудиофокус занят другим приложением: воспроизведение недоступно.");
        }

        return null;
    }

    private void AbandonAudioFocus()
    {
        var request = _focusRequest;
        _focusRequest = null;
        if (request is not null)
        {
            if (OperatingSystem.IsAndroidVersionAtLeast(26))
            {
                AbandonFocusRequest(request);
            }

            return;
        }

#pragma warning disable CA1422 // Obsolete audio focus API для API 24-25.
        try
        {
            _audioManager.AbandonAudioFocus(_focusListener);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Audio focus abandon failed: " + exception.GetType().Name);
        }
#pragma warning restore CA1422
    }

    [SupportedOSPlatform("android26.0")]
    private void AbandonFocusRequest(AudioFocusRequestClass request)
    {
        try
        {
            _audioManager.AbandonAudioFocusRequest(request);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Audio focus abandon failed: " + exception.GetType().Name);
        }
        finally
        {
            request.Dispose();
        }
    }

    private void OnAudioFocusChanged(AudioFocus focusChange)
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        switch (focusChange)
        {
            case AudioFocus.Loss:
            case AudioFocus.LossTransient:
            case AudioFocus.LossTransientCanDuck:
                OnAudioFocusLost();
                break;
            case AudioFocus.Gain:
                OnAudioFocusGained();
                break;
            default:
                break;
        }
    }

    private void OnAudioFocusLost()
    {
        Interlocked.Exchange(ref _focusLost, 1);
        var fault = new Error(
            AudioErrorCodes.PlaybackUnavailable,
            "Воспроизведение приостановлено: аудиофокус передан другому приложению.");
        if (Interlocked.CompareExchange(ref _focusFault, fault, null) is null)
        {
            Log.Warn(LogTag, "Audio focus lost: output paused.");
        }

        lock (_trackGate)
        {
            try
            {
                _track?.Pause();
                _track?.Flush();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "AudioTrack pause failed: " + exception.GetType().Name);
            }
        }
    }

    private void OnAudioFocusGained()
    {
        if (Volatile.Read(ref _stopped) == 1)
        {
            return;
        }

        Interlocked.Exchange(ref _focusLost, 0);
        Interlocked.Exchange(ref _focusFault, null);
        lock (_trackGate)
        {
            var track = _track;
            if (track is null)
            {
                return;
            }

            try
            {
                track.Play();
            }
            catch (Exception exception)
            {
                Log.Warn(LogTag, "AudioTrack resume failed: " + exception.GetType().Name);
                SetTerminalFault(new Error(
                    AudioErrorCodes.PlaybackUnavailable,
                    "Не удалось возобновить воспроизведение после возврата аудиофокуса."));
            }
        }
    }

    private void ThrowIfUnavailable()
    {
        var fault = Volatile.Read(ref _focusFault) ?? Volatile.Read(ref _terminalFault);
        if (fault is not null)
        {
            throw new AndroidAudioAdapterException(fault, "Воспроизведение недоступно.");
        }

        if (Volatile.Read(ref _stopped) == 1)
        {
            throw new ObjectDisposedException(
                GetType().Name,
                "Сессия воспроизведения остановлена.");
        }
    }

    private void SetTerminalFault(Error error)
    {
        if (Interlocked.CompareExchange(ref _terminalFault, error, null) is not null)
        {
            return;
        }

        Log.Warn(LogTag, "Playback stopped: " + error.Code);
        _buffer.Complete();
    }

    private void ReleaseTrack()
    {
        lock (_trackGate)
        {
            var track = _track;
            _track = null;
            if (track is null)
            {
                return;
            }

            ReleaseTrackInstance(track);
        }
    }

    private static void ReleaseTrackInstance(AudioTrack track)
    {
        try
        {
            track.Stop();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioTrack.Stop failed: " + exception.GetType().Name);
        }

        try
        {
            track.Release();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioTrack release failed: " + exception.GetType().Name);
        }
        finally
        {
            track.Dispose();
        }
    }

    private sealed class AudioFocusChangeListener : Java.Lang.Object, AudioManager.IOnAudioFocusChangeListener
    {
        private readonly AndroidAudioPlaybackSession _owner;

        internal AudioFocusChangeListener(AndroidAudioPlaybackSession owner) => _owner = owner;

        public void OnAudioFocusChange(AudioFocus focusChange) => _owner.OnAudioFocusChanged(focusChange);
    }
}
