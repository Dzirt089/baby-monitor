using System.Security.Cryptography;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Публичная identity устройства: SubjectPublicKeyInfo (ECDSA P-256) и SHA-256 fingerprint.
/// Private key никогда не покидает Android Keystore; этот объект содержит только публичные данные.
/// </summary>
public sealed class DeviceIdentityPublicKey : IEquatable<DeviceIdentityPublicKey>
{
    private readonly byte[] _subjectPublicKeyInfo;

    /// <summary>Создаёт публичную identity из SPKI и fingerprint.</summary>
    /// <param name="subjectPublicKeyInfo">DER SubjectPublicKeyInfo.</param>
    /// <param name="fingerprintSha256">Hex lowercase SHA-256 fingerprint.</param>
    /// <exception cref="ArgumentException">Пустые данные или fingerprint.</exception>
    public DeviceIdentityPublicKey(byte[] subjectPublicKeyInfo, string fingerprintSha256)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        ArgumentException.ThrowIfNullOrWhiteSpace(fingerprintSha256);
        if (subjectPublicKeyInfo.Length == 0)
        {
            throw new ArgumentException("SPKI не может быть пустым.", nameof(subjectPublicKeyInfo));
        }

        _subjectPublicKeyInfo = (byte[])subjectPublicKeyInfo.Clone();
        FingerprintSha256 = fingerprintSha256;
    }

    /// <summary>Создаёт identity, вычисляя fingerprint = SHA-256(SPKI).</summary>
    /// <param name="subjectPublicKeyInfo">DER SubjectPublicKeyInfo.</param>
    /// <returns>Публичная identity.</returns>
    public static DeviceIdentityPublicKey FromSubjectPublicKeyInfo(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        var fingerprint = Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant();
        return new DeviceIdentityPublicKey(subjectPublicKeyInfo, fingerprint);
    }

    /// <summary>DER SubjectPublicKeyInfo (копия).</summary>
    public ReadOnlyMemory<byte> SubjectPublicKeyInfo => _subjectPublicKeyInfo;

    /// <summary>Hex lowercase SHA-256 fingerprint публичного ключа.</summary>
    public string FingerprintSha256 { get; }

    /// <inheritdoc />
    public bool Equals(DeviceIdentityPublicKey? other) =>
        other is not null && string.Equals(FingerprintSha256, other.FingerprintSha256, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as DeviceIdentityPublicKey);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(FingerprintSha256);
}
