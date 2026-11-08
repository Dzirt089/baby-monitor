namespace BabyMonitor.Application.Media;

/// <summary>
/// Стабильные коды ошибок media-координатора, дополняющие коды портов
/// (<see cref="Abstractions.Realtime.RealtimeTransportErrorCodes"/>,
/// <see cref="Abstractions.Audio.AudioErrorCodes"/>,
/// <see cref="Abstractions.Discovery.DiscoveryErrorCodes"/>). Без секретов и аудиоданных.
/// </summary>
public static class MediaSessionErrorCodes
{
    /// <summary>Некорректный порядок операций координатора (Start/Stop после Dispose, повторный Stop и т.п.).</summary>
    public const string InvalidState = "media.invalid_state";

    /// <summary>Адаптер завершился неожиданным исключением; детали не раскрываются наружу.</summary>
    public const string AdapterFailure = "media.adapter_failure";

    /// <summary>Для роли нет доверенного peer в trusted store: сначала нужно сопряжение (004/006).</summary>
    public const string NoTrustedPeer = "media.no_trusted_peer";

    /// <summary>Не удалось прочитать trusted store при создании media-сеанса.</summary>
    public const string TrustedStoreFailed = "media.trusted_store_failed";
}
