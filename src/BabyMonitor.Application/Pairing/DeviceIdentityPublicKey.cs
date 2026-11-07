using System.Security.Cryptography;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Публичная identity устройства: SubjectPublicKeyInfo (ECDSA P-256) и SHA-256 fingerprint.
/// Private key никогда не покидает Android Keystore. Конструктор закрыт: fingerprint всегда
/// вычисляется из SPKI, поэтому подмена fingerprint невозможна.
/// </summary>
public sealed class DeviceIdentityPublicKey : IEquatable<DeviceIdentityPublicKey>
{
    private readonly byte[] _subjectPublicKeyInfo;

    private DeviceIdentityPublicKey(byte[] subjectPublicKeyInfo)
    {
        _subjectPublicKeyInfo = subjectPublicKeyInfo;
        FingerprintSha256 = Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo)).ToLowerInvariant();
    }

    /// <summary>Создаёт identity из DER SubjectPublicKeyInfo (P-256).</summary>
    /// <param name="subjectPublicKeyInfo">DER SubjectPublicKeyInfo.</param>
    /// <returns>Публичная identity.</returns>
    /// <exception cref="ArgumentException">SPKI пуст или не является ECDSA P-256 ключом.</exception>
    public static DeviceIdentityPublicKey FromSubjectPublicKeyInfo(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        if (!PairingCrypto.TryValidateIdentityPublicKey(subjectPublicKeyInfo))
        {
            throw new ArgumentException("Ожидался ECDSA P-256 SubjectPublicKeyInfo.", nameof(subjectPublicKeyInfo));
        }

        return new DeviceIdentityPublicKey((byte[])subjectPublicKeyInfo.Clone());
    }

    /// <summary>DER SubjectPublicKeyInfo (immutable view).</summary>
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
