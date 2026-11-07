using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Безопасное для UI представление доверенного устройства: роли, fingerprint и время сопряжения.
/// Не содержит ключей, токенов и персональных данных. Полный fingerprint нужен только операции
/// Forget и намеренно не выводится в <see cref="ToString"/> (production logs не содержат полных
/// fingerprints).
/// </summary>
public sealed record PairedDeviceSummary
{
    private const int ShortFingerprintLength = 8;

    private PairedDeviceSummary(
        DeviceRole localRole,
        DeviceRole peerRole,
        string fingerprintSha256,
        string fingerprintShort,
        DateTimeOffset pairedAt)
    {
        LocalRole = localRole;
        PeerRole = peerRole;
        FingerprintSha256 = fingerprintSha256;
        FingerprintShort = fingerprintShort;
        PairedAt = pairedAt;
    }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole LocalRole { get; }

    /// <summary>Роль доверенного peer (дополняет локальную роль).</summary>
    public DeviceRole PeerRole { get; }

    /// <summary>
    /// Полный hex fingerprint peer для операции Forget. Не логируется: в <see cref="ToString"/>
    /// выводится только короткая форма.
    /// </summary>
    public string FingerprintSha256 { get; }

    /// <summary>Первые 8 hex-символов fingerprint (lowercase) для показа пользователю.</summary>
    public string FingerprintShort { get; }

    /// <summary>Время сопряжения.</summary>
    public DateTimeOffset PairedAt { get; }

    /// <summary>Создаёт безопасное представление доверенного устройства.</summary>
    /// <param name="device">Доверенное устройство.</param>
    /// <returns>Представление для UI.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="device"/> равен <see langword="null"/>.</exception>
    public static PairedDeviceSummary From(PairedDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var fingerprint = device.PeerIdentity.FingerprintSha256;
        var shortFingerprint = fingerprint.Length <= ShortFingerprintLength
            ? fingerprint
            : fingerprint[..ShortFingerprintLength];
        return new PairedDeviceSummary(
            device.LocalRole,
            device.PeerRole,
            fingerprint,
            shortFingerprint.ToLowerInvariant(),
            device.PairedAt);
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"PairedDeviceSummary {{ LocalRole = {LocalRole}, PeerRole = {PeerRole}, FingerprintShort = {FingerprintShort}, PairedAt = {PairedAt:O} }}";
}
