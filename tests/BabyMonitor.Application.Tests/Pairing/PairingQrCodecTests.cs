using BabyMonitor.Application.Pairing;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>QR payload: round-trip, версии, секреты private key не попадают в QR.</summary>
public sealed class PairingQrCodecTests
{
    private static PairingInvitation CreateInvitation(FakePairingKeyStore keys, DateTimeOffset expiresAt)
    {
        using var ephemeral = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var signature = keys.SignAsync([], CancellationToken.None).GetAwaiter().GetResult().Value;
        return new PairingInvitation(
            PairingConstants.ProtocolVersion,
            "test-token",
            expiresAt,
            keys.Identity,
            ephemeral.ExportSubjectPublicKeyInfo(),
            signature);
    }

    [Fact]
    public void QR_round_trip_сохраняет_поля()
    {
        using var keys = new FakePairingKeyStore();
        var invitation = CreateInvitation(keys, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var payload = PairingQrCodec.Encode(invitation);
        var decoded = PairingQrCodec.Decode(payload);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(invitation.Token, decoded.Value.Token);
        Assert.Equal(invitation.ExpiresAt, decoded.Value.ExpiresAt);
        Assert.Equal(invitation.ChildIdentity.FingerprintSha256, decoded.Value.ChildIdentity.FingerprintSha256);
        Assert.Equal(invitation.ChildEphemeralPublicKey, decoded.Value.ChildEphemeralPublicKey);
        Assert.Equal(invitation.ChildSignature, decoded.Value.ChildSignature);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-qr")]
    [InlineData("bm-pair:{}")]
    [InlineData("bm-pair:{\"v\":1}")]
    public void Некорректный_payload_отклоняется(string payload)
    {
        var decoded = PairingQrCodec.Decode(payload);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public void QR_не_содержит_private_key_материала()
    {
        using var keys = new FakePairingKeyStore();
        var invitation = CreateInvitation(keys, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var payload = PairingQrCodec.Encode(invitation);
        var privateKeyBase64 = Convert.ToBase64String(keys.ExportPrivateKeyForAssertions());
        var privateKeyBase64Url = privateKeyBase64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.DoesNotContain(privateKeyBase64Url, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("private", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QR_начинается_с_префикса_и_версии()
    {
        using var keys = new FakePairingKeyStore();
        var invitation = CreateInvitation(keys, DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));

        var payload = PairingQrCodec.Encode(invitation);

        Assert.StartsWith(PairingConstants.QrPrefix + "v1.", payload, StringComparison.Ordinal);
        Assert.DoesNotContain("{", payload, StringComparison.Ordinal);
    }
}

/// <summary>Граничные/негативные случаи QR-декодирования (security).</summary>
public sealed class PairingQrSecurityTests
{
    [Fact]
    public void Повреждённый_SPKI_identity_отклоняется_как_invalid_qr()
    {
        using var keys = new FakePairingKeyStore();
        using var ephemeral = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var invitation = new PairingInvitation(
            PairingConstants.ProtocolVersion,
            "token",
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
            keys.Identity,
            ephemeral.ExportSubjectPublicKeyInfo(),
            [1, 2, 3]);
        var payload = PairingQrCodec.Encode(invitation);
        var body = payload[(PairingConstants.QrPrefix.Length + "v1.".Length)..];
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(body);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var version = reader.ReadInt32();
        var tokenLength = reader.ReadInt32();
        var tokenBytes = reader.ReadBytes(tokenLength);
        var expiresAt = reader.ReadInt64();
        var identityLength = reader.ReadInt32();
        var identityBytes = reader.ReadBytes(identityLength);
        var ephemeralLength = reader.ReadInt32();
        var ephemeralBytes = reader.ReadBytes(ephemeralLength);
        var signatureLength = reader.ReadInt32();
        var signatureBytes = reader.ReadBytes(signatureLength);
        identityBytes[0] ^= 0xFF;

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(version);
            writer.Write(tokenBytes.Length);
            writer.Write(tokenBytes);
            writer.Write(expiresAt);
            writer.Write(identityBytes.Length);
            writer.Write(identityBytes);
            writer.Write(ephemeralBytes.Length);
            writer.Write(ephemeralBytes);
            writer.Write(signatureBytes.Length);
            writer.Write(signatureBytes);
        }

        var malformed = PairingConstants.QrPrefix + "v1." + System.Buffers.Text.Base64Url.EncodeToString(output.ToArray());
        var decoded = PairingQrCodec.Decode(malformed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public void Слишком_большой_payload_отклоняется()
    {
        var decoded = PairingQrCodec.Decode(PairingConstants.QrPrefix + "v1." + new string('A', 20_000));

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }
}

/// <summary>Повреждение ephemeral SPKI в QR (security boundary).</summary>
public sealed class PairingQrEphemeralSecurityTests
{
    [Fact]
    public void Повреждённый_ephemeral_SPKI_отклоняется_как_invalid_qr()
    {
        using var keys = new FakePairingKeyStore();
        using var ephemeral = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var invitation = new PairingInvitation(
            PairingConstants.ProtocolVersion,
            "token",
            DateTimeOffset.FromUnixTimeSeconds(1_800_000_000),
            keys.Identity,
            ephemeral.ExportSubjectPublicKeyInfo(),
            [1, 2, 3]);
        var payload = PairingQrCodec.Encode(invitation);
        var body = payload[(PairingConstants.QrPrefix.Length + "v1.".Length)..];
        var bytes = System.Buffers.Text.Base64Url.DecodeFromChars(body);

        using var stream = new MemoryStream(bytes);
        using var reader = new BinaryReader(stream);
        var version = reader.ReadInt32();
        var tokenLength = reader.ReadInt32();
        var tokenBytes = reader.ReadBytes(tokenLength);
        var expiresAt = reader.ReadInt64();
        var identityLength = reader.ReadInt32();
        var identityBytes = reader.ReadBytes(identityLength);
        var ephemeralLength = reader.ReadInt32();
        var ephemeralBytes = reader.ReadBytes(ephemeralLength);
        var signatureLength = reader.ReadInt32();
        var signatureBytes = reader.ReadBytes(signatureLength);
        ephemeralBytes[0] ^= 0xFF;

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(version);
            writer.Write(tokenBytes.Length);
            writer.Write(tokenBytes);
            writer.Write(expiresAt);
            writer.Write(identityBytes.Length);
            writer.Write(identityBytes);
            writer.Write(ephemeralBytes.Length);
            writer.Write(ephemeralBytes);
            writer.Write(signatureBytes.Length);
            writer.Write(signatureBytes);
        }

        var malformed = PairingConstants.QrPrefix + "v1." + System.Buffers.Text.Base64Url.EncodeToString(output.ToArray());
        var decoded = PairingQrCodec.Decode(malformed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }
}
