using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>
/// Регрессия интероп-дефекта, найденного emulator smoke: Android Keystore подписывает
/// SHA256withECDSA и возвращает DER SEQUENCE (70–72 байта), а BCL Ecdsa.SignData по умолчанию —
/// IEEE P1363 (64 байта для P-256). Протокол обязан принимать обе стандартные кодировки,
/// иначе реальное сопряжение падало invalid_signature при валидных ключах.
/// </summary>
public sealed class PairingSignatureInteropTests
{
    [Fact]
    public async Task Приглашение_Child_с_DER_подписью_Android_Keystore_принимается_Parent()
    {
        using var childKeyStore = new FakePairingKeyStore(derSignatures: true);
        using var parentKeyStore = new FakePairingKeyStore();
        var timeProvider = new MutableTimeProvider();
        var created = await ChildPairingSession.CreateAsync(
            childKeyStore,
            new DeterministicEntropy(),
            timeProvider,
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Code);
        using var child = created.Value;
        Assert.True(child.Invitation.ChildSignature.Length > 64, "DER-подпись должна быть длиннее 64 байт.");

        var accepted = await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeyStore, timeProvider, CancellationToken.None);

        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
        using var parent = accepted.Value;
        var handled = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
        Assert.True(handled.IsSuccess, handled.Error?.Code);
        Assert.Equal(parent.Verification.Code, handled.Value.Code);
    }

    [Fact]
    public async Task Ответ_Parent_с_DER_подписью_Android_Keystore_принимается_Child()
    {
        using var childKeyStore = new FakePairingKeyStore();
        using var parentKeyStore = new FakePairingKeyStore(derSignatures: true);
        var timeProvider = new MutableTimeProvider();
        var created = await ChildPairingSession.CreateAsync(
            childKeyStore,
            new DeterministicEntropy(),
            timeProvider,
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Code);
        using var child = created.Value;
        var accepted = await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeyStore, timeProvider, CancellationToken.None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
        using var parent = accepted.Value;
        Assert.True(parent.Response.ParentSignature.Length > 64, "DER-подпись Parent должна быть длиннее 64 байт.");

        var handled = await child.HandleResponseAsync(parent.Response, CancellationToken.None);

        Assert.True(handled.IsSuccess, handled.Error?.Code);
        Assert.Equal(parent.Verification.Code, handled.Value.Code);
    }

    [Fact]
    public async Task Сопряжение_через_DER_подписи_обеих_сторон_сохраняет_доверие_с_комплементарными_ролями()
    {
        using var childKeyStore = new FakePairingKeyStore(derSignatures: true);
        using var parentKeyStore = new FakePairingKeyStore(derSignatures: true);
        var childStore = new InMemoryTrustedDeviceStore();
        var parentStore = new InMemoryTrustedDeviceStore();
        var timeProvider = new MutableTimeProvider();
        var created = await ChildPairingSession.CreateAsync(
            childKeyStore,
            new DeterministicEntropy(),
            timeProvider,
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Code);
        using var child = created.Value;
        var accepted = await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeyStore, timeProvider, CancellationToken.None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
        using var parent = accepted.Value;
        var handled = await child.HandleResponseAsync(parent.Response, CancellationToken.None);
        Assert.True(handled.IsSuccess, handled.Error?.Code);

        var childConfirmed = await child.ConfirmAsync(handled.Value, userConfirmed: true, childStore, CancellationToken.None);
        var parentConfirmed = await parent.ConfirmAsync(parent.Verification, userConfirmed: true, parentStore, CancellationToken.None);

        Assert.True(childConfirmed.IsSuccess, childConfirmed.Error?.Code);
        Assert.True(parentConfirmed.IsSuccess, parentConfirmed.Error?.Code);
        Assert.Equal(DeviceRole.Child, childConfirmed.Value.LocalRole);
        Assert.Equal(DeviceRole.Parent, parentConfirmed.Value.LocalRole);
        Assert.Equal(parentKeyStore.Identity.FingerprintSha256, childConfirmed.Value.PeerIdentity.FingerprintSha256);
        Assert.Equal(childKeyStore.Identity.FingerprintSha256, parentConfirmed.Value.PeerIdentity.FingerprintSha256);
    }

    [Fact]
    public async Task Подменённая_DER_подпись_ответа_отклоняется()
    {
        using var childKeyStore = new FakePairingKeyStore();
        using var parentKeyStore = new FakePairingKeyStore(derSignatures: true);
        var timeProvider = new MutableTimeProvider();
        var created = await ChildPairingSession.CreateAsync(
            childKeyStore,
            new DeterministicEntropy(),
            timeProvider,
            CancellationToken.None);
        Assert.True(created.IsSuccess, created.Error?.Code);
        using var child = created.Value;
        var accepted = await ParentPairingSession.AcceptAsync(child.QrPayload, parentKeyStore, timeProvider, CancellationToken.None);
        Assert.True(accepted.IsSuccess, accepted.Error?.Code);
        using var parent = accepted.Value;

        var signature = parent.Response.ParentSignature;
        signature[^1] ^= 0xFF;
        var tampered = new PairingResponse(parent.Response.ParentIdentity, parent.Response.ParentEphemeralPublicKey, signature);
        var handled = await child.HandleResponseAsync(tampered, CancellationToken.None);

        Assert.False(handled.IsSuccess, "Подменённая подпись не должна приниматься.");
        Assert.Equal(PairingErrorCodes.InvalidSignature, handled.Error!.Code);
    }
}
