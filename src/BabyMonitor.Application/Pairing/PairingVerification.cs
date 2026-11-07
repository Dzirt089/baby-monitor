namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Результат проверки pairing-транскрипта: verification code для сравнения пользователями и
/// hash транскрипта (не секрет). Shared secret здесь не раскрывается.
/// </summary>
public sealed record PairingVerification
{
    /// <summary>Создаёт результат верификации.</summary>
    /// <param name="code">6-значный verification code.</param>
    /// <param name="transcriptHashSha256">SHA-256 hash полного транскрипта.</param>
    /// <exception cref="ArgumentException">Некорректный код.</exception>
    public PairingVerification(string code, byte[] transcriptHashSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(transcriptHashSha256);
        if (transcriptHashSha256.Length != 32)
        {
            throw new ArgumentException("Transcript hash должен быть SHA-256.", nameof(transcriptHashSha256));
        }

        Code = code;
        TranscriptHashSha256 = (byte[])transcriptHashSha256.Clone();
    }

    /// <summary>6-значный код для визуального сравнения пользователями.</summary>
    public string Code { get; }

    /// <summary>SHA-256 полного транскрипта.</summary>
    public byte[] TranscriptHashSha256 { get; }
}
