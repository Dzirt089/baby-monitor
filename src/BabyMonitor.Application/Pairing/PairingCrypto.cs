using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Криптографические примитивы pairing-протокола на BCL: подписи ECDSA P-256, ECDH P-256,
/// SHA-256, HKDF-SHA256. Все входы с недоверенным SPKI валидируются и не бросают исключения
/// наружу. Собственная криптография не используется; секреты не логируются.
/// </summary>
internal static class PairingCrypto
{
    private const string NistP256Oid = "1.2.840.10045.3.1.7";
    private static readonly byte[] VerificationCodeInfo = Encoding.UTF8.GetBytes("babymonitor-pairing-v1-code");

    public static ECDiffieHellman CreateEphemeralKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public static bool TryValidateIdentityPublicKey(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var read);
            return read == subjectPublicKeyInfo.Length && IsP256(key.ExportParameters(includePrivateParameters: false));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static bool TryValidateEphemeralPublicKey(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        try
        {
            using var key = ECDiffieHellman.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out var read);
            return read == subjectPublicKeyInfo.Length && IsP256(key.ExportParameters(includePrivateParameters: false));
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static byte[] BuildInvitationTranscript(PairingInvitation invitation) =>
        BuildInvitationTranscript(
            invitation.ProtocolVersion,
            invitation.Token,
            invitation.ExpiresAt,
            invitation.ChildIdentity.FingerprintSha256,
            invitation.ChildEphemeralPublicKey);

    public static byte[] BuildInvitationTranscript(
        int protocolVersion,
        string token,
        DateTimeOffset expiresAt,
        string childFingerprintSha256,
        byte[] childEphemeralPublicKey)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(protocolVersion);
        WriteBytes(writer, Encoding.UTF8.GetBytes(token));
        writer.Write(expiresAt.ToUnixTimeSeconds());
        WriteBytes(writer, Encoding.UTF8.GetBytes(childFingerprintSha256));
        WriteBytes(writer, childEphemeralPublicKey);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] BuildResponseTranscript(byte[] invitationTranscriptHash, PairingResponse response) =>
        BuildResponseTranscript(
            invitationTranscriptHash,
            response.ParentIdentity.FingerprintSha256,
            response.ParentEphemeralPublicKey);

    public static byte[] BuildResponseTranscript(
        byte[] invitationTranscriptHash,
        string parentFingerprintSha256,
        byte[] parentEphemeralPublicKey)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteBytes(writer, invitationTranscriptHash);
        WriteBytes(writer, Encoding.UTF8.GetBytes(parentFingerprintSha256));
        WriteBytes(writer, parentEphemeralPublicKey);
        writer.Flush();
        return stream.ToArray();
    }

    public static byte[] CombineTranscripts(byte[] invitationTranscript, byte[] responseTranscript)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            WriteBytes(writer, invitationTranscript);
            WriteBytes(writer, responseTranscript);
            writer.Flush();
        }

        return SHA256.HashData(stream.ToArray());
    }

    /// <summary>
    /// Выводит ECDH shared secret. Может бросить CryptographicException/ArgumentException при
    /// невалидном peer SPKI — вызывающий код обязан преобразовать это в typed error.
    /// </summary>
    public static byte[] DeriveSharedSecret(ECDiffieHellman ownEphemeral, byte[] peerEphemeralSubjectPublicKeyInfo)
    {
        if (!TryValidateEphemeralPublicKey(peerEphemeralSubjectPublicKeyInfo))
        {
            throw new CryptographicException("Peer ephemeral key is not ECDH P-256.");
        }

        using var peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerEphemeralSubjectPublicKeyInfo, out _);
        return ownEphemeral.DeriveKeyMaterial(peer.PublicKey);
    }

    public static string DeriveVerificationCode(byte[] sharedSecret, byte[] combinedTranscriptHash)
    {
        var derived = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 4, combinedTranscriptHash, VerificationCodeInfo);
        var value = BinaryPrimitives.ReadUInt32BigEndian(derived) % PairingConstants.VerificationCodeModulus;
        return value.ToString("D" + PairingConstants.VerificationCodeDigits, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static bool TryVerifySignature(byte[] subjectPublicKeyInfo, byte[] data, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        if (!TryValidateIdentityPublicKey(subjectPublicKeyInfo))
        {
            return false;
        }

        try
        {
            using var key = ECDsa.Create();
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
            return key.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    public static string ToBase64Url(byte[] data) => Base64Url.EncodeToString(data);

    public static bool TryFromBase64Url(string text, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        try
        {
            data = Base64Url.DecodeFromChars(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsP256(ECParameters curve) =>
        string.Equals(curve.Curve.Oid.Value, NistP256Oid, StringComparison.Ordinal)
        && curve.Curve.IsNamed;

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
