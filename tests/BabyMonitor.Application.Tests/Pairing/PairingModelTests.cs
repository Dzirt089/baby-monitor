using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>Инварианты value objects pairing.</summary>
public sealed class PairingModelTests
{
    [Fact]
    public void PairedDevice_выводит_дополняющую_роль_peer()
    {
        using var keys = new FakePairingKeyStore();
        var device = new PairedDevice(keys.Identity, DeviceRole.Child, new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));

        Assert.Equal(DeviceRole.Parent, device.PeerRole);
        Assert.Equal(DeviceRole.Child, device.LocalRole);
    }

    [Theory]
    [InlineData(DeviceRole.Unknown)]
    public void PairedDevice_отклоняет_некорректную_локальную_роль(DeviceRole role)
    {
        using var keys = new FakePairingKeyStore();

        Assert.Throws<ArgumentException>(() => new PairedDevice(keys.Identity, role, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Identity_fingerprint_детерминирован_и_равен_по_fingerprint()
    {
        using var keys = new FakePairingKeyStore();
        var copy = DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(keys.Identity.SubjectPublicKeyInfo.ToArray());

        Assert.Equal(keys.Identity, copy);
        Assert.Equal(keys.Identity.FingerprintSha256, copy.FingerprintSha256);
        Assert.Equal(64, copy.FingerprintSha256.Length);
    }

    [Fact]
    public void Identity_не_раскрывает_изменяемый_массив()
    {
        using var keys = new FakePairingKeyStore();
        var original = keys.Identity.SubjectPublicKeyInfo.ToArray();
        var copy = DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(original);
        original[0] ^= 0xFF;

        Assert.NotEqual(original, copy.SubjectPublicKeyInfo.ToArray());
    }
}
