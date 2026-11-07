using BabyMonitor.Application.Pairing;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>QR payload: round-trip, версии, секреты private key не попадают в QR.</summary>
public sealed class PairingQrCodecTests
{
    private static PairingInvitation CreateInvitation(FakePairingKeyStore keys, DateTimeOffset expiresAt)
    {
        var ephemeral = System.Security.Cryptography.ECDiffieHellman.Create();
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
