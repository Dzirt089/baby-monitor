namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Результат проверки pairing-транскрипта: verification code и hash транскрипта (не секрет).
/// Shared secret здесь не раскрывается; hash отдаётся копией.
/// </summary>
public sealed class PairingVerification
{
    private readonly byte[] _transcriptHashSha256;

    /// <summary>Создаёт результат верификации.</summary>
    /// <param name="code">6-значный verification code.</param>
    /// <param name="transcriptHashSha256">SHA-256 hash полного транскрипта.</param>
    /// <exception cref="ArgumentException">Некорректный код/размер hash.</exception>
    public PairingVerification(string code, byte[] transcriptHashSha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(transcriptHashSha256);
        if (code.Length != PairingConstants.VerificationCodeDigits || !code.All(char.IsAsciiDigit))
        {
            throw new ArgumentException("Verification code должен состоять из 6 цифр.", nameof(code));
        }

        if (transcriptHashSha256.Length != 32)
        {
            throw new ArgumentException("Transcript hash должен быть SHA-256.", nameof(transcriptHashSha256));
        }

        Code = code;
        _transcriptHashSha256 = (byte[])transcriptHashSha256.Clone();
    }

    /// <summary>6-значный код для визуального сравнения пользователями.</summary>
    public string Code { get; }

    /// <summary>SHA-256 полного транскрипта (копия).</summary>
    public byte[] TranscriptHashSha256 => (byte[])_transcriptHashSha256.Clone();
}
