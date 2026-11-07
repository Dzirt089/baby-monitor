using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>
/// Модели экранов сопряжения Child/Parent: полный офлайн-цикл QR-обмена, коды, expiry,
/// отказ пользователя, сбои store и корректный dispose.
/// </summary>
public sealed class PairingScreenModelTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private sealed class PairingScreens : IDisposable
    {
        public PairingScreens(ITrustedDeviceStore? childStore = null, ITrustedDeviceStore? parentStore = null)
        {
            Time = new MutableTimeProvider();
            ChildKeys = new FakePairingKeyStore();
            ParentKeys = new FakePairingKeyStore();
            ChildStore = childStore ?? new InMemoryTrustedDeviceStore();
            ParentStore = parentStore ?? new InMemoryTrustedDeviceStore();
            Child = new ChildPairingScreenModel(ChildKeys, new DeterministicEntropy(), ChildStore, Time);
            Parent = new ParentPairingScreenModel(ParentKeys, ParentStore, Time);
        }

        public MutableTimeProvider Time { get; }

        public FakePairingKeyStore ChildKeys { get; }

        public FakePairingKeyStore ParentKeys { get; }

        public ITrustedDeviceStore ChildStore { get; }

        public ITrustedDeviceStore ParentStore { get; }

        public ChildPairingScreenModel Child { get; }

        public ParentPairingScreenModel Parent { get; }

        public void Dispose()
        {
            Child.Dispose();
            Parent.Dispose();
            ChildKeys.Dispose();
            ParentKeys.Dispose();
        }
    }

    private static async Task<string> CreateInvitationAsync(PairingScreens screens)
    {
        await screens.Child.CreateInvitationAsync(None);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
        Assert.NotNull(screens.Child.QrPayload);
        return screens.Child.QrPayload!;
    }

    private static async Task<string> AcceptInvitationAsync(PairingScreens screens, string invitationQr)
    {
        await screens.Parent.AcceptInvitationAsync(invitationQr, None);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Parent.State);
        Assert.NotNull(screens.Parent.ResponseQrPayload);
        return screens.Parent.ResponseQrPayload!;
    }

    private static async Task AcceptResponseAsync(PairingScreens screens, string responseQr)
    {
        await screens.Child.AcceptResponseAsync(responseQr, None);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Child.State);
        Assert.Null(screens.Child.ErrorCode);
    }

    private static async Task CompletePairingAsync(PairingScreens screens)
    {
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);
        await screens.Child.ConfirmAsync(true, None);
        await screens.Parent.ConfirmAsync(true, None);
        Assert.Equal(PairingFlowState.Completed, screens.Child.State);
        Assert.Equal(PairingFlowState.Completed, screens.Parent.State);
    }

    private static string ShortFingerprint(string fingerprintSha256) => fingerprintSha256[..8];

    [Fact]
    public async Task Полный_офлайн_цикл_сопряжения_завершается_успехом_на_обеих_сторонах()
    {
        using var screens = new PairingScreens();

        Assert.Equal(PairingFlowState.Idle, screens.Child.State);
        Assert.Null(screens.Child.QrPayload);
        Assert.Null(screens.Child.ExpiresAt);
        Assert.Null(screens.Child.RemainingValidity);
        var invitationQr = await CreateInvitationAsync(screens);
        Assert.StartsWith(PairingConstants.QrPrefix, invitationQr, StringComparison.Ordinal);
        Assert.Equal(screens.Time.GetUtcNow() + PairingConstants.InvitationLifetime, screens.Child.ExpiresAt);
        Assert.Equal(PairingConstants.InvitationLifetime, screens.Child.RemainingValidity);
        Assert.Null(screens.Child.VerificationCode);
        Assert.Null(screens.Child.PeerFingerprintShort);
        Assert.Null(screens.Child.ErrorCode);
        Assert.False(screens.Child.IsBusy);

        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        Assert.StartsWith(PairingConstants.ResponseQrPrefix, responseQr, StringComparison.Ordinal);
        Assert.Matches("^[0-9]{6}$", screens.Parent.VerificationCode);
        Assert.Equal(ShortFingerprint(screens.ChildKeys.Identity.FingerprintSha256), screens.Parent.ChildFingerprintShort);
        Assert.Null(screens.Parent.ErrorCode);
        Assert.False(screens.Parent.IsBusy);

        await AcceptResponseAsync(screens, responseQr);
        Assert.Equal(screens.Parent.VerificationCode, screens.Child.VerificationCode);
        Assert.Matches("^[0-9]{6}$", screens.Child.VerificationCode);
        Assert.Equal(ShortFingerprint(screens.ParentKeys.Identity.FingerprintSha256), screens.Child.PeerFingerprintShort);

        await screens.Child.ConfirmAsync(true, None);
        await screens.Parent.ConfirmAsync(true, None);

        Assert.Equal(PairingFlowState.Completed, screens.Child.State);
        Assert.Equal(PairingFlowState.Completed, screens.Parent.State);
        Assert.NotNull(screens.Child.PairedDevice);
        Assert.NotNull(screens.Parent.PairedDevice);

        var childDevices = (await screens.ChildStore.GetAllAsync(None)).Value;
        Assert.Single(childDevices);
        var childSide = childDevices[0];
        Assert.Equal(DeviceRole.Child, childSide.LocalRole);
        Assert.Equal(DeviceRole.Parent, childSide.PeerRole);
        Assert.Equal(screens.ParentKeys.Identity.FingerprintSha256, childSide.PeerIdentity.FingerprintSha256);
        Assert.Equal(screens.Time.GetUtcNow(), childSide.PairedAt);

        var parentDevices = (await screens.ParentStore.GetAllAsync(None)).Value;
        Assert.Single(parentDevices);
        var parentSide = parentDevices[0];
        Assert.Equal(DeviceRole.Parent, parentSide.LocalRole);
        Assert.Equal(DeviceRole.Child, parentSide.PeerRole);
        Assert.Equal(screens.ChildKeys.Identity.FingerprintSha256, parentSide.PeerIdentity.FingerprintSha256);
    }

    [Fact]
    public async Task Повторный_AcceptResponse_после_успеха_отклоняется_invalid_state()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);

        await screens.Child.AcceptResponseAsync(responseQr, None);

        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Child.State);
    }

    [Fact]
    public async Task Повторное_создание_приглашения_во_время_ожидания_отклоняется()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);

        await screens.Child.CreateInvitationAsync(None);

        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
        Assert.Equal(invitationQr, screens.Child.QrPayload);
    }

    [Fact]
    public async Task PollExpiry_просроченного_приглашения_переводит_в_Failed_и_позволяет_создать_новое()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);
        screens.Time.Advance(PairingConstants.InvitationLifetime + TimeSpan.FromSeconds(1));

        screens.Child.PollExpiry();

        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
        Assert.Equal(PairingErrorCodes.Expired, screens.Child.ErrorCode);
        Assert.NotNull(screens.Child.ErrorMessage);
        Assert.Equal(TimeSpan.Zero, screens.Child.RemainingValidity);

        await screens.Child.CreateInvitationAsync(None);

        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
        Assert.Null(screens.Child.ErrorCode);
        Assert.NotNull(screens.Child.QrPayload);
    }

    [Fact]
    public async Task PollExpiry_до_истечения_не_меняет_состояние()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);
        screens.Time.Advance(TimeSpan.FromSeconds(1));

        screens.Child.PollExpiry();

        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
        Assert.Null(screens.Child.ErrorCode);
    }

    [Fact]
    public async Task PollExpiry_идемпотентен_и_не_поднимает_Changed_повторно()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);
        screens.Time.Advance(PairingConstants.InvitationLifetime + TimeSpan.FromSeconds(1));
        var raised = 0;
        screens.Child.Changed += (_, _) => raised++;

        screens.Child.PollExpiry();
        var afterFirstPoll = raised;
        screens.Child.PollExpiry();

        Assert.Equal(1, afterFirstPoll);
        Assert.Equal(afterFirstPoll, raised);
        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
    }

    [Fact]
    public async Task Parent_отклоняет_просроченный_QR_как_expired()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        screens.Time.Advance(PairingConstants.InvitationLifetime + TimeSpan.FromSeconds(1));

        await screens.Parent.AcceptInvitationAsync(invitationQr, None);

        Assert.Equal(PairingFlowState.Failed, screens.Parent.State);
        Assert.Equal(PairingErrorCodes.Expired, screens.Parent.ErrorCode);
        Assert.Null(screens.Parent.ResponseQrPayload);
        Assert.Null(screens.Parent.VerificationCode);
    }

    [Fact]
    public async Task Подмена_подписи_ответа_оставляет_child_в_Waiting_и_даёт_invalid_signature()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        var decoded = PairingResponseQrCodec.Decode(responseQr);
        Assert.True(decoded.IsSuccess);
        var signature = decoded.Value.ParentSignature;
        signature[0] ^= 0xFF;
        var tampered = new PairingResponse(decoded.Value.ParentIdentity, decoded.Value.ParentEphemeralPublicKey, signature);

        await screens.Child.AcceptResponseAsync(PairingResponseQrCodec.Encode(tampered), None);

        Assert.Equal(PairingErrorCodes.InvalidSignature, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
        Assert.Null(screens.Child.VerificationCode);
        Assert.Null(screens.Child.PeerFingerprintShort);
    }

    [Fact]
    public async Task Ответ_с_чужой_identity_и_подписью_другого_parent_отклоняется_invalid_signature()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        var decoded = PairingResponseQrCodec.Decode(responseQr);
        Assert.True(decoded.IsSuccess);
        using var alienKeys = new FakePairingKeyStore();
        var alien = new PairingResponse(alienKeys.Identity, decoded.Value.ParentEphemeralPublicKey, decoded.Value.ParentSignature);

        await screens.Child.AcceptResponseAsync(PairingResponseQrCodec.Encode(alien), None);

        Assert.Equal(PairingErrorCodes.InvalidSignature, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
    }

    [Fact]
    public async Task Повторный_ответ_второго_parent_после_первого_отклоняется_invalid_state()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        var firstResponseQr = await AcceptInvitationAsync(screens, invitationQr);

        using var secondParentKeys = new FakePairingKeyStore();
        using var secondParent = new ParentPairingScreenModel(secondParentKeys, new InMemoryTrustedDeviceStore(), screens.Time);
        await secondParent.AcceptInvitationAsync(invitationQr, None);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, secondParent.State);
        var secondResponseQr = secondParent.ResponseQrPayload!;

        await AcceptResponseAsync(screens, firstResponseQr);
        await screens.Child.AcceptResponseAsync(secondResponseQr, None);

        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Child.State);
    }

    [Fact]
    public async Task Ответ_на_чужое_приглашение_отклоняется_invalid_signature()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);
        using var alienChildKeys = new FakePairingKeyStore();
        using var alienChild = new ChildPairingScreenModel(
            alienChildKeys,
            new DeterministicEntropy(),
            new InMemoryTrustedDeviceStore(),
            screens.Time);
        await alienChild.CreateInvitationAsync(None);
        await screens.Parent.AcceptInvitationAsync(alienChild.QrPayload, None);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Parent.State);

        await screens.Child.AcceptResponseAsync(screens.Parent.ResponseQrPayload, None);

        Assert.Equal(PairingErrorCodes.InvalidSignature, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
    }

    [Fact]
    public async Task Отказ_пользователя_переводит_в_Failed_и_не_сохраняет_доверие()
    {
        using var screens = new PairingScreens();
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);

        await screens.Child.ConfirmAsync(false, None);

        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
        Assert.Equal(PairingErrorCodes.UserRejected, screens.Child.ErrorCode);
        Assert.Null(screens.Child.PairedDevice);
        Assert.Empty((await screens.ChildStore.GetAllAsync(None)).Value);

        await screens.Parent.ConfirmAsync(false, None);

        Assert.Equal(PairingFlowState.Failed, screens.Parent.State);
        Assert.Equal(PairingErrorCodes.UserRejected, screens.Parent.ErrorCode);
        Assert.Null(screens.Parent.PairedDevice);
        Assert.Empty((await screens.ParentStore.GetAllAsync(None)).Value);
    }

    [Fact]
    public async Task Сбой_store_оставляет_child_в_Awaiting_и_позволяет_повторить()
    {
        using var screens = new PairingScreens(childStore: new FailingTrustedDeviceStore());
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);

        await screens.Child.ConfirmAsync(true, None);

        Assert.Equal(PairingErrorCodes.StoreFailed, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Child.State);
        Assert.Null(screens.Child.PairedDevice);
    }

    [Fact]
    public async Task Сбой_store_оставляет_parent_в_Awaiting()
    {
        using var screens = new PairingScreens(parentStore: new FailingTrustedDeviceStore());
        var invitationQr = await CreateInvitationAsync(screens);
        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);

        await screens.Parent.ConfirmAsync(true, None);

        Assert.Equal(PairingErrorCodes.StoreFailed, screens.Parent.ErrorCode);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Parent.State);
        Assert.Null(screens.Parent.PairedDevice);
    }

    [Fact]
    public async Task Confirm_без_ответа_отклоняется_invalid_state()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);

        await screens.Child.ConfirmAsync(true, None);

        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.WaitingForPeerResponse, screens.Child.State);
    }

    [Fact]
    public async Task Cancel_переводит_в_Failed_и_запрещает_дальнейшие_шаги()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);
        var raised = 0;
        screens.Child.Changed += (_, _) => raised++;

        screens.Child.Cancel();

        Assert.True(raised > 0);
        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
        Assert.Equal(PairingErrorCodes.Cancelled, screens.Child.ErrorCode);

        await screens.Child.AcceptResponseAsync("bm-pair-resp:v1.AAAA", None);

        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
    }

    [Fact]
    public async Task Cancel_после_завершения_не_меняет_состояние()
    {
        using var screens = new PairingScreens();
        await CompletePairingAsync(screens);

        screens.Child.Cancel();
        screens.Parent.Cancel();

        Assert.Equal(PairingFlowState.Completed, screens.Child.State);
        Assert.Equal(PairingFlowState.Completed, screens.Parent.State);
        Assert.Null(screens.Child.ErrorCode);
        Assert.Null(screens.Parent.ErrorCode);
    }

    [Fact]
    public async Task Dispose_идемпотентен_и_методы_после_него_дают_invalid_state()
    {
        using var screens = new PairingScreens();
        await CreateInvitationAsync(screens);

        screens.Child.Dispose();
        screens.Child.Dispose();

        await screens.Child.CreateInvitationAsync(None);
        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Contains("освобождена", screens.Child.ErrorMessage, StringComparison.Ordinal);

        await screens.Child.AcceptResponseAsync("bm-pair-resp:v1.AAAA", None);
        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);

        await screens.Child.ConfirmAsync(true, None);
        Assert.Equal(PairingErrorCodes.InvalidState, screens.Child.ErrorCode);
        Assert.Equal(PairingFlowState.Failed, screens.Child.State);
    }

    [Fact]
    public async Task Parent_методы_после_Dispose_дают_invalid_state()
    {
        using var screens = new PairingScreens();
        screens.Parent.Dispose();
        screens.Parent.Dispose();

        await screens.Parent.AcceptInvitationAsync(null, None);
        Assert.Equal(PairingErrorCodes.InvalidState, screens.Parent.ErrorCode);
        Assert.Equal(PairingFlowState.Failed, screens.Parent.State);

        await screens.Parent.ConfirmAsync(true, None);
        Assert.Equal(PairingErrorCodes.InvalidState, screens.Parent.ErrorCode);
    }

    [Fact]
    public async Task Parent_повторный_accept_после_ошибки_сбрасывает_ошибку()
    {
        using var screens = new PairingScreens();
        await screens.Parent.AcceptInvitationAsync("broken-qr", None);
        Assert.Equal(PairingFlowState.Failed, screens.Parent.State);
        Assert.Equal(PairingErrorCodes.InvalidQr, screens.Parent.ErrorCode);

        var invitationQr = await CreateInvitationAsync(screens);
        await screens.Parent.AcceptInvitationAsync(invitationQr, None);

        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Parent.State);
        Assert.Null(screens.Parent.ErrorCode);
        Assert.NotNull(screens.Parent.ResponseQrPayload);
        Assert.Matches("^[0-9]{6}$", screens.Parent.VerificationCode);
    }

    [Fact]
    public async Task Ошибка_keystore_при_создании_приглашения_переводит_в_Failed()
    {
        using var model = new ChildPairingScreenModel(
            new FailingPairingKeyStore(),
            new DeterministicEntropy(),
            new InMemoryTrustedDeviceStore(),
            new MutableTimeProvider());

        await model.CreateInvitationAsync(None);

        Assert.Equal(PairingFlowState.Failed, model.State);
        Assert.Equal(PairingErrorCodes.KeystoreUnavailable, model.ErrorCode);
        Assert.Null(model.QrPayload);
        Assert.Null(model.ExpiresAt);
        Assert.False(model.IsBusy);
    }

    [Fact]
    public async Task Changed_поднимается_при_успешных_переходах()
    {
        using var screens = new PairingScreens();
        var raised = 0;
        screens.Child.Changed += (_, _) => raised++;

        var invitationQr = await CreateInvitationAsync(screens);
        Assert.True(raised > 0);
        var afterCreate = raised;

        var responseQr = await AcceptInvitationAsync(screens, invitationQr);
        await AcceptResponseAsync(screens, responseQr);

        Assert.True(raised > afterCreate);
        Assert.Equal(PairingFlowState.AwaitingUserConfirmation, screens.Child.State);
    }

    private sealed class FailingPairingKeyStore : IPairingKeyStore
    {
        public Task<Result<DeviceIdentityPublicKey>> GetOrCreateIdentityAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result<DeviceIdentityPublicKey>.Failure(
                new Error(PairingErrorCodes.KeystoreUnavailable, "Android Keystore недоступен.")));

        public Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken) =>
            Task.FromResult(Result<byte[]>.Failure(
                new Error(PairingErrorCodes.KeystoreUnavailable, "Android Keystore недоступен.")));
    }
}
