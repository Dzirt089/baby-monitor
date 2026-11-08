using Android.Content;
using Android.Media;
using Android.Util;
using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Platform.Android.Audio;

/// <summary>
/// Воспроизведение на родительском устройстве (Parent) через Android <c>AudioTrack</c> за портом
/// <see cref="IAudioPlayback"/>: 48 kHz mono 16-bit PCM, audio focus, системный маршрут вывода.
/// </summary>
/// <remarks>
/// Инвариант роли: адаптер предназначен только для Parent; Child playback endpoint не создаёт
/// (AGENTS.md §2, §7, §20). Speakerphone не форсируется: <c>AudioAttributes</c>
/// USAGE_MEDIA/CONTENT_TYPE_SPEECH выводят звук на текущее системное устройство (динамик,
/// наушники, Bluetooth), а изменение маршрута выполняет audio framework. Audio focus
/// удерживается на время сессии; потеря focus останавливает воспроизведение и возвращается
/// типизированной ошибкой <see cref="AudioErrorCodes.PlaybackUnavailable"/>. PCM не пишется на
/// диск и не логируется; логируются только типы ошибок.
/// </remarks>
public sealed class AndroidAudioPlayback : IAudioPlayback
{
    private const string LogTag = "BabyMonitor.Audio";
    private const int FrameDurationMilliseconds = 20;

    private readonly Context _context;

    /// <summary>Создаёт адаптер воспроизведения.</summary>
    /// <param name="context">Android context приложения (Activity не удерживается).</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    public AndroidAudioPlayback(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task<Result<IAudioPlaybackSession>> StartAsync(
        AudioPlaybackOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();

        var audioManager = _context.ApplicationContext?.GetSystemService(Context.AudioService) as AudioManager
            ?? _context.GetSystemService(Context.AudioService) as AudioManager;
        if (audioManager is null)
        {
            return Result<IAudioPlaybackSession>.Failure(new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Аудиосистема устройства недоступна."));
        }

        var audioAttributes = CreateAudioAttributes();
        if (audioAttributes is null)
        {
            return Result<IAudioPlaybackSession>.Failure(new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось настроить параметры вывода звука."));
        }

        var capacityFrames = Math.Max(1, options.PreferredBufferMilliseconds / FrameDurationMilliseconds);
        AndroidAudioPlaybackSession session;
        try
        {
            session = new AndroidAudioPlaybackSession(
                audioManager,
                audioAttributes,
                options.Format,
                capacityFrames);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Playback buffer allocation failed: " + exception.GetType().Name);
            return Result<IAudioPlaybackSession>.Failure(new Error(
                AudioErrorCodes.BufferUnavailable,
                "Не удалось выделить буфер воспроизведения звука."));
        }

        Error? startError;
        try
        {
            startError = session.TryStart();
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "AudioTrack start failed: " + exception.GetType().Name);
            await session.DisposeAsync().ConfigureAwait(false);
            return Result<IAudioPlaybackSession>.Failure(new Error(
                AudioErrorCodes.PlaybackUnavailable,
                "Не удалось запустить воспроизведение звука."));
        }

        if (startError is not null)
        {
            await session.DisposeAsync().ConfigureAwait(false);
            return Result<IAudioPlaybackSession>.Failure(startError);
        }

        return Result<IAudioPlaybackSession>.Success(session);
    }

    // Speakerphone/route не задаются принудительно: USAGE_MEDIA + CONTENT_TYPE_SPEECH выводятся на
    // системный маршрут по умолчанию. Отдельная настройка маршрута — только по решению устройства.
    private static AudioAttributes? CreateAudioAttributes()
    {
        var builder = new AudioAttributes.Builder();
        builder.SetUsage(AudioUsageKind.Media);
        builder.SetContentType(AudioContentType.Speech);
        return builder.Build();
    }
}
