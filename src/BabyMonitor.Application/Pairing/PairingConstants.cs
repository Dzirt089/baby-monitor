namespace BabyMonitor.Application.Pairing;

/// <summary>Константы протокола сопряжения (004).</summary>
public static class PairingConstants
{
    /// <summary>Версия pairing-протокола; несовпадение отклоняется.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Время жизни приглашения (token).</summary>
    public static readonly TimeSpan InvitationLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Количество цифр verification code.</summary>
    public const int VerificationCodeDigits = 6;

    /// <summary>Модуль для verification code (10^digits).</summary>
    public const int VerificationCodeModulus = 1_000_000;

    /// <summary>Префикс QR-payload.</summary>
    public const string QrPrefix = "bm-pair:";

    /// <summary>Префикс QR-payload ответа Parent (офлайн QR-обмен 006).</summary>
    public const string ResponseQrPrefix = "bm-pair-resp:";
}
