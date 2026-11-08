namespace BabyMonitor.Application.Abstractions.Audio;

/// <summary>Стабильные коды ошибок аудио-портов (без секретов и аудиоданных).</summary>
public static class AudioErrorCodes
{
    /// <summary>Нет разрешения на микрофон (RECORD_AUDIO) у Child.</summary>
    public const string CapturePermissionDenied = "audio.capture_permission_denied";

    /// <summary>Захват недоступен: микрофон занят/отсутствует.</summary>
    public const string CaptureUnavailable = "audio.capture_unavailable";

    /// <summary>Воспроизведение недоступно: устройство вывода/audio focus.</summary>
    public const string PlaybackUnavailable = "audio.playback_unavailable";

    /// <summary>Захват или воспроизведение завершились ошибкой платформы.</summary>
    public const string AdapterFailed = "audio.adapter_failed";

    /// <summary>Некорректное использование порта (роль/формат/повторный старт).</summary>
    public const string InvalidState = "audio.invalid_state";

    /// <summary>Не удалось выделить/удержать bounded buffer аудио.</summary>
    public const string BufferUnavailable = "audio.buffer_unavailable";
}
