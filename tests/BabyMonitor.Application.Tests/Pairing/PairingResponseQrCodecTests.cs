using System.Buffers.Text;
using System.Security.Cryptography;
using BabyMonitor.Application.Pairing;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>QR-код ответа Parent: round-trip, лимиты, повреждения и отсутствие секретов в payload/ошибках.</summary>
public sealed class PairingResponseQrCodecTests
{
    private static async Task<PairingResponse> CreateResponseAsync(FakePairingKeyStore keys)
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var signature = (await keys.SignAsync([1, 2, 3], CancellationToken.None)).Value;
        return new PairingResponse(keys.Identity, ephemeral.ExportSubjectPublicKeyInfo(), signature);
    }

    private static byte[] CreateEphemeralSpki()
    {
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return ephemeral.ExportSubjectPublicKeyInfo();
    }

    private static byte[] DecodeBody(string payload) =>
        Base64Url.DecodeFromChars(payload.AsSpan(PairingConstants.ResponseQrPrefix.Length + "v1.".Length));

    [Fact]
    public async Task QR_ответа_round_trip_сохраняет_identity_ключ_и_подпись()
    {
        using var keys = new FakePairingKeyStore();
        var response = await CreateResponseAsync(keys);

        var payload = PairingResponseQrCodec.Encode(response);
        var decoded = PairingResponseQrCodec.Decode(payload);

        Assert.True(decoded.IsSuccess);
        Assert.Equal(response.ParentIdentity.FingerprintSha256, decoded.Value.ParentIdentity.FingerprintSha256);
        Assert.Equal(
            response.ParentIdentity.SubjectPublicKeyInfo.ToArray(),
            decoded.Value.ParentIdentity.SubjectPublicKeyInfo.ToArray());
        Assert.Equal(response.ParentEphemeralPublicKey, decoded.Value.ParentEphemeralPublicKey);
        Assert.Equal(response.ParentSignature, decoded.Value.ParentSignature);
    }

    [Fact]
    public async Task QR_ответа_начинается_с_префикса_версии_и_не_содержит_private_key()
    {
        using var keys = new FakePairingKeyStore();
        var response = await CreateResponseAsync(keys);

        var payload = PairingResponseQrCodec.Encode(response);
        var privateKeyBase64 = Convert.ToBase64String(keys.ExportPrivateKeyForAssertions());
        var privateKeyBase64Url = privateKeyBase64.TrimEnd('=').Replace('+', '-').Replace('/', '_');

        Assert.StartsWith(PairingConstants.ResponseQrPrefix + "v1.", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(privateKeyBase64Url, payload, StringComparison.Ordinal);
        Assert.DoesNotContain("private", payload, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bm-pair:v1.AAAA")]
    [InlineData("bm-pair-resp:v2.AAAA")]
    [InlineData("bm-pair-resp:not-base64!")]
    [InlineData("bm-pair-resp:v1.")]
    public void Некорректный_payload_ответа_отклоняется(string? payload)
    {
        var decoded = PairingResponseQrCodec.Decode(payload);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public async Task Обрезанный_payload_ответа_отклоняется()
    {
        using var keys = new FakePairingKeyStore();
        var payload = PairingResponseQrCodec.Encode(await CreateResponseAsync(keys));

        var decoded = PairingResponseQrCodec.Decode(payload[..^8]);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public async Task Лишние_байты_в_конце_payload_ответа_отклоняются()
    {
        using var keys = new FakePairingKeyStore();
        var payload = PairingResponseQrCodec.Encode(await CreateResponseAsync(keys));
        var bytes = DecodeBody(payload);
        var extended = new byte[bytes.Length + 1];
        bytes.CopyTo(extended, 0);
        extended[^1] = 0x42;
        var malformed = PairingConstants.ResponseQrPrefix + "v1." + Base64Url.EncodeToString(extended);

        var decoded = PairingResponseQrCodec.Decode(malformed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public async Task Невалидный_SPKI_identity_ответа_отклоняется()
    {
        using var keys = new FakePairingKeyStore();
        var payload = PairingResponseQrCodec.Encode(await CreateResponseAsync(keys));
        var bytes = DecodeBody(payload);
        bytes[4] ^= 0xFF;
        var malformed = PairingConstants.ResponseQrPrefix + "v1." + Base64Url.EncodeToString(bytes);

        var decoded = PairingResponseQrCodec.Decode(malformed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public async Task Невалидный_ephemeral_SPKI_ответа_отклоняется()
    {
        using var keys = new FakePairingKeyStore();
        var payload = PairingResponseQrCodec.Encode(await CreateResponseAsync(keys));
        var bytes = DecodeBody(payload);
        var identityLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
        var ephemeralLengthOffset = 4 + identityLength;
        var ephemeralOffset = ephemeralLengthOffset + 4;
        bytes[ephemeralOffset] ^= 0xFF;
        var malformed = PairingConstants.ResponseQrPrefix + "v1." + Base64Url.EncodeToString(bytes);

        var decoded = PairingResponseQrCodec.Decode(malformed);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
    }

    [Fact]
    public void Encode_null_бросает_ArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => PairingResponseQrCodec.Encode(null!));
    }

    [Fact]
    public void Encode_превышение_лимита_поля_бросает_ArgumentException()
    {
        using var keys = new FakePairingKeyStore();
        var response = new PairingResponse(keys.Identity, CreateEphemeralSpki(), new byte[4097]);

        Assert.Throws<ArgumentException>(() => PairingResponseQrCodec.Encode(response));
    }

    [Fact]
    public void Encode_превышение_общего_лимита_payload_бросает_ArgumentException()
    {
        using var keys = new FakePairingKeyStore();
        var response = new PairingResponse(keys.Identity, new byte[4096], new byte[4096]);

        Assert.Throws<ArgumentException>(() => PairingResponseQrCodec.Encode(response));
    }

    [Fact]
    public void Ошибка_разбора_не_содержит_payload_ответа()
    {
        const string marker = "SECRET_TOKEN_MARKER_123";

        var decoded = PairingResponseQrCodec.Decode(PairingConstants.ResponseQrPrefix + "v1." + marker);

        Assert.False(decoded.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidQr, decoded.Error!.Code);
        Assert.DoesNotContain(marker, decoded.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(marker, decoded.Error.Code, StringComparison.Ordinal);
    }
}
