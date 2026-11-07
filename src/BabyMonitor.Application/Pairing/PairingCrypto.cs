using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Криптографические примитивы pairing-протокола на BCL: подписи ECDSA P-256, ECDH P-256,
/// SHA-256, HKDF-SHA256. Собственная криптография не используется; секреты не логируются.
/// </summary>
internal static class PairingCrypto
{
    private static readonly byte[] VerificationCodeInfo = Encoding.UTF8.GetBytes("babymonitor-pairing-v1-code");

    public static ECDsa CreateIdentityKey() => ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public static ECDiffieHellman CreateEphemeralKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

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
        stream.Write(invitationTranscript);
        stream.Write(responseTranscript);
        return SHA256.HashData(stream.ToArray());
    }

    public static byte[] DeriveSharedSecret(
        ECDiffieHellman ownEphemeral,
        byte[] peerEphemeralSubjectPublicKeyInfo)
    {
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

    public static bool VerifySignature(byte[] subjectPublicKeyInfo, byte[] data, byte[] signature)
    {
        using var key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);
        return key.VerifyData(data, signature, HashAlgorithmName.SHA256);
    }

    public static string ToBase64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryFromBase64Url(string text, out byte[] data)
    {
        data = [];
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var normalized = text.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            0 => string.Empty,
            _ => "invalid"
        };
        if (normalized.EndsWith("invalid", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            data = Convert.FromBase64String(normalized);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
