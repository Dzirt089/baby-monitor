using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Identity устройства для signaling: локальная долгосрочная identity (через
/// <see cref="IPairingKeyStore"/>), ожидаемая identity peer из trusted store и операция подписи
/// identity assertion. Ожидаемый fingerprint передаётся composition root-ом из pairing/trusted
/// store: доверять peer по IP, mDNS-имени или сертификату TLS запрещено (ADR-0007).
/// </summary>
public sealed class SignalingIdentity
{
    private const int Sha256HexLength = 64;

    private readonly IPairingKeyStore _pairingKeyStore;
    private readonly ITrustedDeviceStore _trustedDeviceStore;
    private readonly SemaphoreSlim _identityLock = new(1, 1);
    private DeviceIdentityPublicKey? _localIdentity;

    /// <summary>Создаёт identity для signaling.</summary>
    /// <param name="pairingKeyStore">Хранилище локальной identity и операция подписи (Android Keystore).</param>
    /// <param name="trustedDeviceStore">Хранилище доверенных устройств (pinning peer identity).</param>
    /// <param name="expectedPeerFingerprintSha256">Hex lowercase SHA-256 fingerprint ожидаемого peer из trusted store.</param>
    /// <exception cref="ArgumentNullException">Аргумент равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Fingerprint не является SHA-256 в hex-формате.</exception>
    public SignalingIdentity(
        IPairingKeyStore pairingKeyStore,
        ITrustedDeviceStore trustedDeviceStore,
        string expectedPeerFingerprintSha256)
    {
        ArgumentNullException.ThrowIfNull(pairingKeyStore);
        ArgumentNullException.ThrowIfNull(trustedDeviceStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPeerFingerprintSha256);

        var normalized = expectedPeerFingerprintSha256.ToLowerInvariant();
        if (normalized.Length != Sha256HexLength || !IsHex(normalized))
        {
            throw new ArgumentException("Ожидался hex fingerprint SHA-256 (64 символа).", nameof(expectedPeerFingerprintSha256));
        }

        _pairingKeyStore = pairingKeyStore;
        _trustedDeviceStore = trustedDeviceStore;
        ExpectedPeerFingerprintSha256 = normalized;
    }

    /// <summary>Hex lowercase fingerprint ожидаемой identity peer из trusted store.</summary>
    public string ExpectedPeerFingerprintSha256 { get; }

    /// <summary>Возвращает (и кэширует) локальную публичную identity устройства.</summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Публичная identity либо типизированная ошибка.</returns>
    public async Task<Result<DeviceIdentityPublicKey>> GetLocalIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (Volatile.Read(ref _localIdentity) is { } cached)
        {
            return Result<DeviceIdentityPublicKey>.Success(cached);
        }

        await _identityLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_localIdentity is not null)
            {
                return Result<DeviceIdentityPublicKey>.Success(_localIdentity);
            }

            var identityResult = await _pairingKeyStore
                .GetOrCreateIdentityAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!identityResult.IsSuccess)
            {
                return Result<DeviceIdentityPublicKey>.Failure(SignalingErrors.TlsFailure());
            }

            _localIdentity = identityResult.Value;
            return Result<DeviceIdentityPublicKey>.Success(_localIdentity);
        }
        finally
        {
            _identityLock.Release();
        }
    }

    /// <summary>
    /// Загружает identity ожидаемого peer из trusted store. Отсутствие записи или отказ store
    /// означают недоверенного peer: соединение отклоняется до SDP/media.
    /// </summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Публичная identity доверенного peer либо <c>unauthorized_peer</c>.</returns>
    public async Task<Result<DeviceIdentityPublicKey>> ResolveTrustedPeerAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var findResult = await _trustedDeviceStore
            .FindAsync(ExpectedPeerFingerprintSha256, cancellationToken)
            .ConfigureAwait(false);

        if (!findResult.IsSuccess || findResult.Value is null)
        {
            return Result<DeviceIdentityPublicKey>.Failure(SignalingErrors.UnauthorizedPeer());
        }

        var peerIdentity = findResult.Value.PeerIdentity;
        if (!string.Equals(peerIdentity.FingerprintSha256, ExpectedPeerFingerprintSha256, StringComparison.Ordinal))
        {
            return Result<DeviceIdentityPublicKey>.Failure(SignalingErrors.UnauthorizedPeer());
        }

        return Result<DeviceIdentityPublicKey>.Success(peerIdentity);
    }

    /// <summary>Подписывает identity assertion долгосрочным private key (ECDSA P-256, SHA-256).</summary>
    /// <param name="data">Транскрипт assertion.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <returns>Подпись (P1363 или DER) либо типизированная ошибка.</returns>
    public async Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();

        var signResult = await _pairingKeyStore.SignAsync(data, cancellationToken).ConfigureAwait(false);
        if (!signResult.IsSuccess || signResult.Value.Length == 0)
        {
            return Result<byte[]>.Failure(SignalingErrors.TlsFailure());
        }

        return signResult;
    }

    private static bool IsHex(string value)
    {
        foreach (var character in value)
        {
            if (!char.IsAsciiHexDigitLower(character) && !char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
