namespace BabyMonitor.Application.Pairing;

/// <summary>Стабильные коды ошибок pairing-протокола.</summary>
public static class PairingErrorCodes
{
    /// <summary>QR не распознан/повреждён.</summary>
    public const string InvalidQr = "pairing.invalid_qr";

    /// <summary>Неподдерживаемая версия протокола.</summary>
    public const string UnsupportedVersion = "pairing.unsupported_version";

    /// <summary>Истёк срок действия приглашения.</summary>
    public const string Expired = "pairing.expired";

    /// <summary>Приглашение/token уже использованы.</summary>
    public const string TokenReused = "pairing.token_reused";

    /// <summary>Подпись identity не прошла проверку.</summary>
    public const string InvalidSignature = "pairing.invalid_signature";

    /// <summary>Identity/fingerprint не совпали с ожидаемыми.</summary>
    public const string IdentityMismatch = "pairing.identity_mismatch";

    /// <summary>Verification code не совпал.</summary>
    public const string CodeMismatch = "pairing.code_mismatch";

    /// <summary>Пользователь отклонил сопряжение.</summary>
    public const string UserRejected = "pairing.user_rejected";

    /// <summary>Не удалось сохранить trusted device.</summary>
    public const string StoreFailed = "pairing.store_failed";

    /// <summary>Android Keystore недоступен или операция подписи не удалась.</summary>
    public const string KeystoreUnavailable = "pairing.keystore_unavailable";

    /// <summary>Порядок операций протокола нарушен.</summary>
    public const string InvalidState = "pairing.invalid_state";
}
