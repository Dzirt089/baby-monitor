using System.Security.Cryptography;
using System.Text;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Сессия сопряжения Child: одноразовое приглашение, проверка ответа Parent, вывод verification
/// code, сохранение доверия после подтверждения пользователя. Операции сериализованы; token
/// одноразовый; Dispose освобождает эфемерный ключ и зануляет транскрипт с token.
/// </summary>
public sealed class ChildPairingSession : IDisposable
{
    private enum SessionState
    {
        Created,
        Responded,
        Confirmed,
        Failed
    }

    private readonly IPairingKeyStore _keyStore;
    private readonly TimeProvider _timeProvider;
    private readonly ECDiffieHellman _ephemeral;
    private readonly byte[] _invitationTranscript;
    private readonly byte[] _invitationTranscriptHash;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionState _state = SessionState.Created;
    private PairingVerification? _verification;
    private DeviceIdentityPublicKey? _peerIdentity;
    private int _disposed;

    private ChildPairingSession(
        IPairingKeyStore keyStore,
        TimeProvider timeProvider,
        ECDiffieHellman ephemeral,
        PairingInvitation invitation,
        byte[] invitationTranscript)
    {
        _keyStore = keyStore;
        _timeProvider = timeProvider;
        _ephemeral = ephemeral;
        Invitation = invitation;
        _invitationTranscript = invitationTranscript;
        _invitationTranscriptHash = SHA256.HashData(invitationTranscript);
    }

    /// <summary>Приглашение Child.</summary>
    public PairingInvitation Invitation { get; }

    /// <summary>QR-payload приглашения (секрет: не логировать).</summary>
    public string QrPayload => PairingQrCodec.Encode(Invitation);

    /// <summary>Создаёт pairing-сессию Child с одноразовым token.</summary>
    /// <param name="keyStore">Хранилище identity (Android Keystore в production).</param>
    /// <param name="entropy">Источник случайности для token.</param>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Сессия либо типизированная ошибка.</returns>
    public static async Task<Result<ChildPairingSession>> CreateAsync(
        IPairingKeyStore keyStore,
        IPairingEntropy entropy,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(entropy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        var identity = await keyStore.GetOrCreateIdentityAsync(cancellationToken);
        if (!identity.IsSuccess)
        {
            return Result<ChildPairingSession>.Failure(identity.Error!);
        }

        var tokenBytes = entropy.GetBytes(32);
        if (tokenBytes is not { Length: >= 32 })
        {
            return Result<ChildPairingSession>.Failure(new Error(
                PairingErrorCodes.InvalidState,
                "Недостаточно случайности для pairing token."));
        }

        var token = PairingCrypto.ToBase64Url(tokenBytes);
        CryptographicOperations.ZeroMemory(tokenBytes);
        var expiresAt = timeProvider.GetUtcNow() + PairingConstants.InvitationLifetime;
        var ephemeral = PairingCrypto.CreateEphemeralKey();
        var ephemeralPublicKey = ephemeral.ExportSubjectPublicKeyInfo();
        var transcript = PairingCrypto.BuildInvitationTranscript(
            PairingConstants.ProtocolVersion,
            token,
            expiresAt,
            identity.Value.FingerprintSha256,
            ephemeralPublicKey);

        var signature = await keyStore.SignAsync(transcript, cancellationToken);
        if (!signature.IsSuccess)
        {
            ephemeral.Dispose();
            return Result<ChildPairingSession>.Failure(signature.Error!);
        }

        var invitation = new PairingInvitation(
            PairingConstants.ProtocolVersion,
            token,
            expiresAt,
            identity.Value,
            ephemeralPublicKey,
            signature.Value);

        return Result<ChildPairingSession>.Success(
            new ChildPairingSession(keyStore, timeProvider, ephemeral, invitation, transcript));
    }

    /// <summary>Проверяет ответ Parent и вычисляет verification code.</summary>
    /// <param name="response">Ответ Parent.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Verification code либо типизированная ошибка.</returns>
    public async Task<Result<PairingVerification>> HandleResponseAsync(
        PairingResponse response,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state is not SessionState.Created)
            {
                return Result<PairingVerification>.Failure(new Error(
                    PairingErrorCodes.TokenReused,
                    "Приглашение уже использовано или сессия завершена."));
            }

            if (_timeProvider.GetUtcNow() > Invitation.ExpiresAt)
            {
                _state = SessionState.Failed;
                return Result<PairingVerification>.Failure(new Error(
                    PairingErrorCodes.Expired,
                    "Срок действия приглашения истёк."));
            }

            var parentPublicKey = response.ParentIdentity.SubjectPublicKeyInfo.ToArray();
            var responseTranscript = PairingCrypto.BuildResponseTranscript(_invitationTranscriptHash, response);
            if (!PairingCrypto.TryValidateIdentityPublicKey(parentPublicKey)
                || !PairingCrypto.TryVerifySignature(parentPublicKey, responseTranscript, response.ParentSignature))
            {
                return Result<PairingVerification>.Failure(new Error(
                    PairingErrorCodes.InvalidSignature,
                    "Подпись родительского устройства не прошла проверку."));
            }

            byte[] sharedSecret;
            try
            {
                sharedSecret = PairingCrypto.DeriveSharedSecret(_ephemeral, response.ParentEphemeralPublicKey);
            }
            catch (Exception exception) when (exception is CryptographicException or ArgumentException)
            {
                return Result<PairingVerification>.Failure(new Error(
                    PairingErrorCodes.InvalidSignature,
                    "Не удалось вывести общий секрет с родительским устройством."));
            }

            var combinedTranscriptHash = PairingCrypto.CombineTranscripts(_invitationTranscript, responseTranscript);
            var code = PairingCrypto.DeriveVerificationCode(sharedSecret, combinedTranscriptHash);
            CryptographicOperations.ZeroMemory(sharedSecret);
            CryptographicOperations.ZeroMemory(responseTranscript);

            _peerIdentity = response.ParentIdentity;
            _verification = new PairingVerification(code, combinedTranscriptHash);
            _state = SessionState.Responded;
            return Result<PairingVerification>.Success(_verification);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Подтверждает код пользователем и сохраняет доверенное устройство.</summary>
    /// <param name="verification">Verification, полученный из HandleResponseAsync.</param>
    /// <param name="userConfirmed">Пользователь подтвердил совпадение кода.</param>
    /// <param name="trustedDeviceStore">Хранилище доверенных устройств.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>PairedDevice либо типизированная ошибка.</returns>
    public async Task<Result<PairedDevice>> ConfirmAsync(
        PairingVerification verification,
        bool userConfirmed,
        ITrustedDeviceStore trustedDeviceStore,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(verification);
        ArgumentNullException.ThrowIfNull(trustedDeviceStore);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_state is not SessionState.Responded || _verification is null || _peerIdentity is null)
            {
                return Result<PairedDevice>.Failure(new Error(
                    PairingErrorCodes.InvalidState,
                    "Pairing-сессия не готова к подтверждению."));
            }

            if (!userConfirmed)
            {
                _state = SessionState.Failed;
                return Result<PairedDevice>.Failure(new Error(
                    PairingErrorCodes.UserRejected,
                    "Пользователь отклонил сопряжение."));
            }

            if (!CodesEqual(verification.Code, _verification.Code))
            {
                return Result<PairedDevice>.Failure(new Error(
                    PairingErrorCodes.CodeMismatch,
                    "Verification code не совпал."));
            }

            var device = new PairedDevice(_peerIdentity, DeviceRole.Child, _timeProvider.GetUtcNow());
            var saved = await trustedDeviceStore.SaveAsync(device, cancellationToken);
            if (!saved.IsSuccess)
            {
                return Result<PairedDevice>.Failure(saved.Error!);
            }

            _state = SessionState.Confirmed;
            return Result<PairedDevice>.Success(device);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Освобождает эфемерный ключ и зануляет транскрипт с token.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _ephemeral.Dispose();
        CryptographicOperations.ZeroMemory(_invitationTranscript);
        CryptographicOperations.ZeroMemory(_invitationTranscriptHash);
        _gate.Dispose();
    }

    private static bool CodesEqual(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
