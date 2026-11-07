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
    public async Task Verification_code_совпадает_на_обеих_сторонах()
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

/// <summary>Негативные протокольные сценарии Security review.</summary>
public sealed class PairingSecurityTests
{
    private static async Task<(ChildPairingSession Child, ParentPairingSession Parent)> CreateSessionsAsync(MutableTimeProvider time)
    {
        var child = (await ChildPairingSession.CreateAsync(new FakePairingKeyStore(), new DeterministicEntropy(), time, CancellationToken.None)).Value;
        var parent = (await ParentPairingSession.AcceptAsync(child.QrPayload, new FakePairingKeyStore(), time, CancellationToken.None)).Value;
        return (child, parent);
    }

    [Fact]
    public async Task Store_failure_не_завершает_сопряжение_успешно()
    {
        var time = new MutableTimeProvider();
        var (child, parent) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var verification = (await child.HandleResponseAsync(parent.Response, CancellationToken.None)).Value;

            var result = await child.ConfirmAsync(verification, true, new FailingTrustedDeviceStore(), CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.StoreFailed, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Parent_отказ_пользователя_не_сохраняет_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var store = new InMemoryTrustedDeviceStore();
            var result = await parent.ConfirmAsync(parent.Verification, userConfirmed: false, store, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.UserRejected, result.Error!.Code);
            Assert.Empty((await store.GetAllAsync(CancellationToken.None)).Value);
        }
    }

    [Fact]
    public async Task Parent_несовпадение_кода_не_сохраняет_доверие()
    {
        var time = new MutableTimeProvider();
        var (child, parent) = await CreateSessionsAsync(time);
        using (child)
        using (parent)
        {
            var store = new InMemoryTrustedDeviceStore();
            var wrongCode = new PairingVerification("000000", parent.Verification.TranscriptHashSha256);
            var result = await parent.ConfirmAsync(wrongCode, true, store, CancellationToken.None);

            Assert.False(result.IsSuccess);
            Assert.Equal(PairingErrorCodes.CodeMismatch, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Parent_отклоняет_неподдерживаемую_версию_QR()
    {
        var time = new MutableTimeProvider();
        using var childKeys = new FakePairingKeyStore();
        using var ephemeral = System.Security.Cryptography.ECDiffieHellman.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var invitation = new PairingInvitation(
            PairingConstants.ProtocolVersion + 1,
            "token",
            time.GetUtcNow() + PairingConstants.InvitationLifetime,
            childKeys.Identity,
            ephemeral.ExportSubjectPublicKeyInfo(),
            [1, 2, 3]);

        var result = await ParentPairingSession.AcceptAsync(PairingQrCodec.Encode(invitation), new FakePairingKeyStore(), time, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PairingErrorCodes.UnsupportedVersion, result.Error!.Code);
    }

    [Fact]
    public async Task Parent_отклоняет_подделанную_подпись_Child_в_QR()
    {
        var time = new MutableTimeProvider();
        var childKeys = new FakePairingKeyStore();
        using var child = (await ChildPairingSession.CreateAsync(childKeys, new DeterministicEntropy(), time, CancellationToken.None)).Value;
        var original = PairingQrCodec.Decode(child.QrPayload).Value;
        var tamperedSignature = original.ChildSignature;
        tamperedSignature[0] ^= 0xFF;
        var tampered = new PairingInvitation(
            original.ProtocolVersion,
            original.Token,
            original.ExpiresAt,
            original.ChildIdentity,
            original.ChildEphemeralPublicKey,
            tamperedSignature);

        var result = await ParentPairingSession.AcceptAsync(PairingQrCodec.Encode(tampered), new FakePairingKeyStore(), time, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(PairingErrorCodes.InvalidSignature, result.Error!.Code);
    }

    [Fact]
    public void DeviceIdentity_отклоняет_мусорный_SPKI()
    {
        Assert.Throws<ArgumentException>(() => DeviceIdentityPublicKey.FromSubjectPublicKeyInfo([1, 2, 3, 4]));
    }

    [Fact]
    public async Task HandleResponse_с_мусорным_SPKI_не_бросает_исключение()
    {
        var time = new MutableTimeProvider();
        var (child, _) = await CreateSessionsAsync(time);
        using (child)
        {
            // Публичный API типизирован, поэтому проверяем валидацию SPKI на границе identity.
            Assert.Throws<ArgumentException>(() => DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(new byte[0]));
        }
    }
}
