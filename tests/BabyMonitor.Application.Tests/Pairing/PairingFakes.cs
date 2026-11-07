using System.Security.Cryptography;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Tests.Pairing;

/// <summary>
/// Test double IPairingKeyStore: ECDSA P-256 в памяти; private key доступен только тесту.
/// derSignatures=true эмулирует Android Keystore (SHA256withECDSA возвращает DER SEQUENCE),
/// по умолчанию BCL-формат IEEE P1363: оба формата обязаны приниматься протоколом.
/// </summary>
internal sealed class FakePairingKeyStore : IPairingKeyStore, IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly DSASignatureFormat _signatureFormat;

    public FakePairingKeyStore(bool derSignatures = false)
    {
        _signatureFormat = derSignatures
            ? DSASignatureFormat.Rfc3279DerSequence
            : DSASignatureFormat.IeeeP1363FixedFieldConcatenation;
        Identity = DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(_key.ExportSubjectPublicKeyInfo());
    }

    public DeviceIdentityPublicKey Identity { get; }

    public Task<Result<DeviceIdentityPublicKey>> GetOrCreateIdentityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<DeviceIdentityPublicKey>.Success(Identity));

    public Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken) =>
        Task.FromResult(Result<byte[]>.Success(_key.SignData(data, HashAlgorithmName.SHA256, _signatureFormat)));

    public byte[] ExportPrivateKeyForAssertions() => _key.ExportPkcs8PrivateKey();

    public void Dispose() => _key.Dispose();
}

/// <summary>Детерминированный источник случайности для воспроизводимых тестов.</summary>
internal sealed class DeterministicEntropy : IPairingEntropy
{
    private byte _counter;

    public byte[] GetBytes(int count)
    {
        var bytes = new byte[count];
        for (var index = 0; index < count; index++)
        {
            bytes[index] = _counter++;
        }

        return bytes;
    }
}

/// <summary>Управляемое время для тестов expiry.</summary>
internal sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan delta) => _now += delta;
}

/// <summary>In-memory trusted store для тестов.</summary>
internal sealed class InMemoryTrustedDeviceStore : ITrustedDeviceStore
{
    private readonly Dictionary<string, PairedDevice> _devices = new(StringComparer.Ordinal);

    public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.TryGetValue(fingerprintSha256, out var device)
            ? Result<PairedDevice?>.Success(device)
            : Result<PairedDevice?>.Success(null));

    public Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PairedDevice>>.Success(_devices.Values.ToArray()));

    public Task<Result> SaveAsync(PairedDevice device, CancellationToken cancellationToken)
    {
        _devices[device.PeerIdentity.FingerprintSha256] = device;
        return Task.FromResult(Result.Success());
    }

    public Task<Result> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken)
    {
        _devices.Remove(fingerprintSha256);
        return Task.FromResult(Result.Success());
    }

    public Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Success(_devices.ContainsKey(fingerprintSha256)));
}

/// <summary>Trusted store, который всегда падает (проверка store_failed).</summary>
internal sealed class FailingTrustedDeviceStore : ITrustedDeviceStore
{
    public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(Result<PairedDevice?>.Success(null));

    public Task<Result<IReadOnlyList<PairedDevice>>> GetAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<IReadOnlyList<PairedDevice>>.Success([]));

    public Task<Result> SaveAsync(PairedDevice device, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(new Error(PairingErrorCodes.StoreFailed, "Trusted store недоступен.")));

    public Task<Result> RemoveAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure(new Error(PairingErrorCodes.StoreFailed, "Trusted store недоступен.")));

    public Task<Result<bool>> IsTrustedAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(Result<bool>.Success(false));
}
