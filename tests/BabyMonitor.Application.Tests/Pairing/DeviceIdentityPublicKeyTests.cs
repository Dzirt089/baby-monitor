using System.Security.Cryptography;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>
/// Проверка интероп-хелпера идентичности (используется signaling/TLS identity assertion 005 phase 2):
/// VerifySignature обязан принимать обе стандартные кодировки ECDSA-подписи (BCL IEEE P1363 и
/// Android Keystore DER) и не принимать подмену.
/// </summary>
public sealed class DeviceIdentityPublicKeyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifySignature_принимает_IEEE_P1363_и_DER_подписи(bool derSignatures)
    {
        using var keyStore = new FakePairingKeyStore(derSignatures: derSignatures);
        var data = RandomNumberGenerator.GetBytes(96);
        var signature = await keyStore.SignAsync(data, CancellationToken.None);

        Assert.True(signature.IsSuccess, signature.Error?.Code);
        Assert.True(keyStore.Identity.VerifySignature(data, signature.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VerifySignature_отклоняет_подменённую_подпись(bool derSignatures)
    {
        using var keyStore = new FakePairingKeyStore(derSignatures: derSignatures);
        var data = RandomNumberGenerator.GetBytes(96);
        var signature = (await keyStore.SignAsync(data, CancellationToken.None)).Value;
        signature[^1] ^= 0xFF;

        Assert.False(keyStore.Identity.VerifySignature(data, signature));
    }

    [Fact]
    public async Task VerifySignature_отклоняет_пустую_подпись_и_не_бросает()
    {
        using var keyStore = new FakePairingKeyStore();
        var data = RandomNumberGenerator.GetBytes(32);

        Assert.False(keyStore.Identity.VerifySignature(data, ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public async Task VerifySignature_отклоняет_подпись_над_другими_данными()
    {
        using var keyStore = new FakePairingKeyStore(derSignatures: true);
        var signedData = RandomNumberGenerator.GetBytes(64);
        var otherData = RandomNumberGenerator.GetBytes(64);
        var signature = (await keyStore.SignAsync(signedData, CancellationToken.None)).Value;

        Assert.False(keyStore.Identity.VerifySignature(otherData, signature));
    }
}
