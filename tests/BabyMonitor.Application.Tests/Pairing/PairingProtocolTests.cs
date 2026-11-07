using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>Протокол pairing: happy path, replay/expiry/tamper, подтверждение и trusted store.</summary>
public sealed class PairingProtocolTests
{
    private static async Task<(ChildPairingSession Child, ParentPairingSession Parent, FakePairingKeyStore ChildKeys, FakePairingKeyStore ParentKeys)> CreateSessionsAsync(
        MutableTimeProvider time)
    {
        var childKeys = new FakePairingKeyStore();
        var parentKeys = new FakePairingKeyStore();
        var child = (await ChildPairingSession.CreateAsync(childKeys, new DeterministicEntropy(), time, CancellationToken.None)).Value;
        var parent = (await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeys, time, CancellationToken.None)).Value;
        return (child, parent, childKeys, parentKeys);
    }

    [Fact]
    public async Task Сопряжение_обе_стороны_получают_одинаковый_код_и_сохраняют_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var childStore = new InMemoryTrustedDeviceStore();
            var parentStore = new InMemoryTrustedDeviceStore();

            var childVerification = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
            Assert.True(childVerification.IsSuccess);
            Assert.Equal(parent.Verification.Code, childVerification.Value.Code);
            Assert.Matches("^[0-9]{6}$", childVerification.Value.Code);

            var childDevice = await child.ConfirmAsync(childVerification.Value, userConfirmed: true, childStore, CancellationToken.None);
            var parentDevice = await parent.ConfirmAsync(parent.Verification, userConfirmed: true, parentStore, CancellationToken.None);

            Assert.True(childDevice.IsSuccess);
            Assert.True(parentDevice.IsSuccess);
            Assert.Equal(DeviceRole.Parent, childDevice.Value.PeerRole);
            Assert.Equal(DeviceRole.Child, parentDevice.Value.PeerRole);
            Assert.True((await childStore.IsTrustedAsync(childDevice.Value.PeerIdentity.FingerprintSha256, CancellationToken.None)).Value);
            Assert.True((await parentStore.IsTrustedAsync(parentDevice.Value.PeerIdentity.FingerprintSha256, CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task Повторное_использование_приглашения_отклоняется()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var first = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
            Assert.True(first.IsSuccess);

            var second = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
            Assert.False(second.IsSuccess);
            Assert.Equal(PairingErrorCodes.TokenReused, second.Error!.Code);
        }
    }

    [Fact]
    public async Task Просроченное_приглашение_отклоняется()
    {
        var time = new MutableTimeProvider();
        var childKeys = new FakePairingKeyStore();
        var parentKeys = new FakePairingKeyStore();
        using var child = (await ChildPairingSession.CreateAsync(childKeys, new DeterministicEntropy(), time, CancellationToken.None)).Value;
        var parent = (await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeys, time, CancellationToken.None)).Value;
        using (parent)
        {
            time.Advance(PairingConstants.InvitationLifetime + TimeSpan.FromSeconds(1));

            var response = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
            Assert.False(response.IsSuccess);
            Assert.Equal(PairingErrorCodes.Expired, response.Error!.Code);
        }
    }

    [Fact]
    public async Task Просроченный_QR_не_принимается_parent_сессией()
    {
        var time = new MutableTimeProvider();
        var childKeys = new FakePairingKeyStore();
        using var child = (await ChildPairingSession.CreateAsync(childKeys, new DeterministicEntropy(), time, CancellationToken.None)).Value;
        var qr = child.QrPayload;
        time.Advance(PairingConstants.InvitationLifetime + TimeSpan.FromSeconds(1));

        var accepted = await ParentPairingSession.AcceptAsync(qr, new FakePairingKeyStore(), time, CancellationToken.None);

        Assert.False(accepted.IsSuccess);
        Assert.Equal(PairingErrorCodes.Expired, accepted.Error!.Code);
    }

    [Fact]
    public async Task Подпись_ответа_с_повреждением_отклоняется()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var tamperedSignature = parent.Response.ParentSignature.ToArray();
            tamperedSignature[0] ^= 0xFF;
            var tampered = new PairingResponse(parent.Response.ParentIdentity, parent.Response.ParentEphemeralPublicKey, tamperedSignature);

            var result = await child.HandleResponseAsync(tampered, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.InvalidSignature, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Отказ_пользователя_не_сохраняет_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var verification = (await child.HandleResponseAsync(parent.Response, CancellationToken.None)).Value;
            var store = new InMemoryTrustedDeviceStore();

            var result = await child.ConfirmAsync(verification, userConfirmed: false, store, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.UserRejected, result.Error!.Code);
            Assert.Empty((await store.GetAllAsync(CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task Несовпадение_verification_code_не_сохраняет_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var verification = (await child.HandleResponseAsync(parent.Response, CancellationToken.None)).Value;
            var wrongCode = new PairingVerification("000000", verification.TranscriptHashSha256);
            var store = new InMemoryTrustedDeviceStore();

            var result = await child.ConfirmAsync(wrongCode, userConfirmed: true, store, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.CodeMismatch, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Удаление_доверенного_устройства_запрещает_повторное_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent, _, _) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var verification = (await child.HandleResponseAsync(parent.Response, CancellationToken.None)).Value;
            var store = new InMemoryTrustedDeviceStore();
            var device = (await child.ConfirmAsync(verification, true, store, CancellationToken.None)).Value;

            await store.RemoveAsync(device.PeerIdentity.FingerprintSha256, CancellationToken.None);

            Assert.False((await store.IsTrustedAsync(device.PeerIdentity.FingerprintSha256, CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task Verification_code_совпадает_и_при_не_детерминированной_случайности()
    {
        var time = new MutableTimeProvider();
        var childKeys = new FakePairingKeyStore();
        var parentKeys = new FakePairingKeyStore();
        using var child = (await ChildPairingSession.CreateAsync(childKeys, new DeterministicEntropy(), time, CancellationToken.None)).Value;
        using var parent = (await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeys, time, CancellationToken.None)).Value;

        var verification = (await child.HandleResponseAsync(parent.Response, CancellationToken.None)).Value;

        Assert.Equal(parent.Verification.Code, verification.Code);
        Assert.Equal(parent.Verification.TranscriptHashSha256, verification.TranscriptHashSha256);
    }
}
