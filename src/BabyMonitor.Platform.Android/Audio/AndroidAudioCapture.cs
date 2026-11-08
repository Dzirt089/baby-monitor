using Android.Content;
using Android.Content.PM;
using Android.Media;
using Android.Util;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Results;
using BabyMonitor.Platform.Android.Permissions;
using AppAudioFormat = BabyMonitor.Application.Abstractions.Audio.AudioFormat;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Захват микрофона детского устройства (Child) через Android <c>AudioRecord</c> за портом
/// <see cref="IAudioCapture"/>: 48 kHz mono 16-bit PCM, кадры 20 мс, bounded-буфер.
/// </summary>
/// <remarks>
/// Инвариант роли: адаптер предназначен только для Child. Parent-роль не регистрирует его в
/// composition root и не вызывает <see cref="StartAsync"/>, поэтому микрофон в Parent не
/// открывается (AGENTS.md §2, §7, §20). Дополнительная страховка — проверка runtime-разрешения
/// RECORD_AUDIO до создания <c>AudioRecord</c>: без разрешения микрофон не открывается.
/// <para>
/// PCM-данные не пишутся на диск и не логируются; логируются только типы ошибок. Выбор audio
/// source — измеряемая настройка (<see cref="DefaultAudioSource"/>), а не скрытое предположение:
/// VOICE_COMMUNICATION может включать AEC/шумоподавление/AGC на части устройств, MIC даёт более
/// «сырой» сигнал; выбор проверяется device-тестами.
/// </para>
/// </remarks>
public sealed class AndroidAudioCapture : IAudioCapture
{
    private const string LogTag = "BabyMonitor.Audio";
    private const int FrameDurationMilliseconds = 20;

    /// <summary>
    /// Audio source по умолчанию для Child. Измеряемая настройка: VOICE_COMMUNICATION даёт
    /// типичную для речи обработку (AEC/NS/AGC) на поддерживающих устройствах; MIC — без неё.
    /// </summary>
    public const AudioSource DefaultAudioSource = global::Android.Media.AudioSource.VoiceCommunication;

    private readonly Context _context;
    private readonly AudioSource _audioSource;

    /// <summary>Создаёт адаптер захвата микрофона.</summary>
    /// <param name="context">Android context приложения (Activity не удерживается).</param>
    /// <param name="audioSource">Измеряемая настройка audio source; по умолчанию <see cref="DefaultAudioSource"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public AndroidAudioCapture(Context context, AudioSource audioSource = DefaultAudioSource)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
        _audioSource = audioSource;
    }

    /// <inheritdoc />
    public async Task<Result<IAudioCaptureSession>> StartAsync(
        AudioCaptureOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        // Parent этот адаптер не вызывает (инвариант роли зафиксирован в composition root). Здесь
        // проверка разрешения нужна, чтобы не открывать микрофон без RECORD_AUDIO и вернуть
        // типизированную ошибку вместо исключения платформы.
        if (_context.CheckSelfPermission(AndroidMicrophonePermission.RecordAudioPermission) != Permission.Granted)
        {
            return Result<IAudioCaptureSession>.Failure(new Error(
                AudioErrorCodes.CapturePermissionDenied,
                "Нет разрешения на микрофон: детское устройство не сможет передавать звук."));
        }

        var capacityFrames = Math.Max(1, options.PreferredBufferMilliseconds / FrameDurationMilliseconds);
        var record = TryCreateRecord(options.Format, capacityFrames, out var creationError);
        if (creationError is not null)
        {
            ReleaseRecord(record);
            return Result<IAudioCaptureSession>.Failure(creationError);
        }

        AndroidAudioCaptureSession session;
        try
        {
            session = new AndroidAudioCaptureSession(record!, options.Format, capacityFrames);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Capture buffer allocation failed: " + exception.GetType().Name);
            ReleaseRecord(record);
            return Result<IAudioCaptureSession>.Failure(new Error(
                AudioErrorCodes.BufferUnavailable,
                "Не удалось выделить буфер захвата звука."));
        }

        Error? startError;
        try
        {
            startError = session.TryStart();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Capture start failed: " + exception.GetType().Name);
            await session.DisposeAsync().ConfigureAwait(false);
            return Result<IAudioCaptureSession>.Failure(new Error(
                AudioErrorCodes.CaptureUnavailable,
                "Не удалось запустить захват звука."));
        }

        if (startError is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return Result<IAudioCaptureSession>.Failure(startError);
        }

        return Result<IAudioCaptureSession>.Success(session);
    }

    private AudioRecord? TryCreateRecord(AppAudioFormat format, int capacityFrames, out Error? error)
    {
        error = null;
        AudioRecord? record = null;
        try
        {
            var minBufferSize = AudioRecord.GetMinBufferSize(
                format.SampleRateHz,
                ChannelIn.Mono,
                Encoding.Pcm16bit);
            if (minBufferSize <= 0)
            {
                error = new Error(
                    AudioErrorCodes.CaptureUnavailable,
                    "Микрофон не поддерживает формат 48 kHz mono 16 bit.");
                return null;
            }

            var bufferSizeBytes = Math.Max(minBufferSize, format.Frame20MsBytes * capacityFrames);

            // Binding помечает возвращаемые значения fluent-методов и Build() nullable; фактически
            // Android возвращает непустой объект либо бросает исключение, но проверяем явно и
            // возвращаем типизированную ошибку вместо null-reference.
            var audioFormatBuilder = new global::Android.Media.AudioFormat.Builder();
            audioFormatBuilder.SetEncoding(Encoding.Pcm16bit);
            audioFormatBuilder.SetSampleRate(format.SampleRateHz);
            audioFormatBuilder.SetChannelMask(InputMonoChannelMask);
            var audioFormat = audioFormatBuilder.Build();
            if (audioFormat is null)
            {
                error = new Error(
                    AudioErrorCodes.CaptureUnavailable,
                    "Не удалось задать формат захвата 48 kHz mono 16 bit.");
                return null;
            }

            var recordBuilder = new AudioRecord.Builder();
            recordBuilder.SetAudioSource(_audioSource);
            recordBuilder.SetAudioFormat(audioFormat);
            recordBuilder.SetBufferSizeInBytes(bufferSizeBytes);
            record = recordBuilder.Build();
            if (record is null)
            {
                error = new Error(
                    AudioErrorCodes.CaptureUnavailable,
                    "Микрофон недоступен: устройство занято или не поддерживает захват.");
                return null;
            }

            if (record.State != State.Initialized)
            {
                error = new Error(
                    AudioErrorCodes.CaptureUnavailable,
                    "Микрофон недоступен: устройство занято или не поддерживает захват.");
                return record;
            }

            return record;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioRecord init failed: " + exception.GetType().Name);
            error = new Error(
                AudioErrorCodes.CaptureUnavailable,
                "Не удалось открыть микрофон: устройство занято или недоступно.");
            return record;
        }
    }

    private static void ReleaseRecord(AudioRecord? record)
    {
        if (record is null)
        {
            return;
        }

        try
        {
            record.Release();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioRecord release failed: " + exception.GetType().Name);
        }
        finally
        {
            record.Dispose();
        }
    }

    // Binding AudioFormat.Builder.SetChannelMask принимает только ChannelOut, хотя микрофону нужен
    // входной CHANNEL_IN_MONO (0x10). Берём типизированное значение ChannelIn.Mono и приводим его
    // численно, а не задаём «магическое» число: при обновлении binding это место легко проверить.
    private const ChannelOut InputMonoChannelMask = (ChannelOut)(int)ChannelIn.Mono;
}
