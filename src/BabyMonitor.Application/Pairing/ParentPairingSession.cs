using System.Security.Cryptography;
using System.Text;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Сессия сопряжения Parent: принимает QR Child, проверяет версию/срок/подпись, формирует
/// подписанный ответ, выводит verification code и сохраняет доверие после подтверждения.
/// Подтверждение сериализовано; Dispose освобождает эфемерный ключ.
/// </summary>
public sealed class ParentPairingSession : IDisposable
{
    private enum SessionState
    {
        AwaitingConfirmation,
        Confirmed,
        Failed
    }

    private readonly IPairingKeyStore _keyStore;
    private readonly TimeProvider _timeProvider;
    private readonly ECDiffieHellman _ephemeral;
    private readonly PairingResponse _response;
    private readonly PairingVerification _verification;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private SessionState _state = SessionState.AwaitingConfirmation;
    private int _disposed;

    private ParentPairingSession(
        IPairingKeyStore keyStore,
        TimeProvider timeProvider,
        ECDiffieHellman ephemeral,
        PairingInvitation invitation,
        PairingResponse response,
        PairingVerification verification)
    {
        _keyStore = keyStore;
        _timeProvider = timeProvider;
        _ephemeral = ephemeral;
        Invitation = invitation;
        _response = response;
        _verification = verification;
    }

    /// <summary>Приглашение Child, из которого построена сессия.</summary>
    public PairingInvitation Invitation { get; }

    /// <summary>Verification code для визуального сравнения.</summary>
    public PairingVerification Verification => _verification;

    /// <summary>Ответ Parent для передачи Child.</summary>
    public PairingResponse Response => _response;

    /// <summary>Принимает QR-payload Child и проверяет приглашение.</summary>
    /// <param name="qrPayload">QR-payload.</param>
    /// <param name="keyStore">Хранилище identity Parent.</param>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <param name="cancellationToken">Отмена.</param>
    /// <returns>Сессия либо типизированная ошибка.</returns>
    public static async Task<Result<ParentPairingSession>> AcceptAsync(
        string? qrPayload,
        IPairingKeyStore keyStore,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        cancellationToken.ThrowIfCancellationRequested();

        var decoded = PairingQrCodec.Decode(qrPayload);
        if (!decoded.IsSuccess)
        {
            return Result<ParentPairingSession>.Failure(decoded.Error!);
        }

        var invitation = decoded.Value;
        if (invitation.ProtocolVersion != PairingConstants.ProtocolVersion)
        {
            return Result<ParentPairingSession>.Failure(new Error(
                PairingErrorCodes.UnsupportedVersion,
                "Версия протокола сопряжения не поддерживается."));
        }

        if (timeProvider.GetUtcNow() > invitation.ExpiresAt)
        {
            return Result<ParentPairingSession>.Failure(new Error(
                PairingErrorCodes.Expired,
                "Срок действия приглашения истёк."));
        }

        var invitationTranscript = PairingCrypto.BuildInvitationTranscript(invitation);
        if (!PairingCrypto.TryVerifySignature(
                invitation.ChildIdentity.SubjectPublicKeyInfo.ToArray(),
                invitationTranscript,
                invitation.ChildSignature))
        {
            CryptographicOperations.ZeroMemory(invitationTranscript);
            return Result<ParentPairingSession>.Failure(new Error(
                PairingErrorCodes.InvalidSignature,
                "Подпись детского устройства не прошла проверку."));
        }

        var identity = await keyStore.GetOrCreateIdentityAsync(cancellationToken);
        if (!identity.IsSuccess)
        {
            return Result<ParentPairingSession>.Failure(identity.Error!);
        }

        var ephemeral = PairingCrypto.CreateEphemeralKey();
        var ephemeralPublicKey = ephemeral.ExportSubjectPublicKeyInfo();
        var invitationTranscriptHash = SHA256.HashData(invitationTranscript);
        var responseTranscript = PairingCrypto.BuildResponseTranscript(
            invitationTranscriptHash,
            identity.Value.FingerprintSha256,
            ephemeralPublicKey);

        var signature = await keyStore.SignAsync(responseTranscript, cancellationToken);
        if (!signature.IsSuccess)
        {
            ephemeral.Dispose();
            return Result<ParentPairingSession>.Failure(signature.Error!);
        }

        var response = new PairingResponse(identity.Value, ephemeralPublicKey, signature.Value);
        byte[] sharedSecret;
        try
        {
            sharedSecret = PairingCrypto.DeriveSharedSecret(ephemeral, invitation.ChildEphemeralPublicKey);
        }
        catch (Exception exception) when (exception is CryptographicException or ArgumentException)
        {
            ephemeral.Dispose();
            CryptographicOperations.ZeroMemory(invitationTranscript);
            CryptographicOperations.ZeroMemory(responseTranscript);
            return Result<ParentPairingSession>.Failure(new Error(
                PairingErrorCodes.InvalidSignature,
                "Не удалось вывести общий секрет с детским устройством."));
        }

        var combinedTranscriptHash = PairingCrypto.CombineTranscripts(invitationTranscript, responseTranscript);
        var code = PairingCrypto.DeriveVerificationCode(sharedSecret, combinedTranscriptHash);
        CryptographicOperations.ZeroMemory(sharedSecret);
        CryptographicOperations.ZeroMemory(invitationTranscript);
        CryptographicOperations.ZeroMemory(responseTranscript);

        var session = new ParentPairingSession(
            keyStore,
            timeProvider,
            ephemeral,
            invitation,
            response,
            new PairingVerification(code, combinedTranscriptHash));
        return Result<ParentPairingSession>.Success(session);
    }

    /// <summary>Подтверждает код пользователем и сохраняет доверенное устройство.</summary>
    /// <param name="verification">Verification, показанный пользователю.</param>
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
            if (Volatile.Read(ref _disposed) == 1)
            {
                return Result<PairedDevice>.Failure(new Error(
                    PairingErrorCodes.InvalidState,
                    "Pairing-сессия уже освобождена."));
            }

            if (_state is not SessionState.AwaitingConfirmation)
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

            var device = new PairedDevice(Invitation.ChildIdentity, DeviceRole.Parent, _timeProvider.GetUtcNow());
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

    /// <summary>
    /// Освобождает эфемерный ключ сессии. Dispose не должен вызываться конкурентно с активными
    /// ConfirmAsync: сначала завершите операции.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _ephemeral.Dispose();
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
