using System.Security.Cryptography;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Test double <see cref="IPairingKeyStore"/>: ECDSA P-256 в памяти. По умолчанию подпись в формате
/// IEEE P1363 (BCL), derSignatures=true эмулирует Android Keystore (SHA256withECDSA → DER SEQUENCE):
/// signaling обязан принимать обе кодировки.
/// </summary>
internal sealed class FakePairingKeyStore : IPairingKeyStore, IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly DSASignatureFormat _signatureFormat;
    private readonly bool _failSigning;

    public FakePairingKeyStore(bool derSignatures = false, bool failSigning = false)
    {
        _signatureFormat = derSignatures
            ? DSASignatureFormat.Rfc3279DerSequence
            : DSASignatureFormat.IeeeP1363FixedFieldConcatenation;
        _failSigning = failSigning;
        Identity = DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(_key.ExportSubjectPublicKeyInfo());
    }

    public DeviceIdentityPublicKey Identity { get; }

    public string FingerprintSha256 => Identity.FingerprintSha256;

    public Task<Result<DeviceIdentityPublicKey>> GetOrCreateIdentityAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Result<DeviceIdentityPublicKey>.Success(Identity));

    public Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken)
    {
        if (_failSigning)
        {
            return Task.FromResult(Result<byte[]>.Failure(
                new Error(PairingErrorCodes.KeystoreUnavailable, "Keystore недоступен.")));
        }

        return Task.FromResult(Result<byte[]>.Success(_key.SignData(data, HashAlgorithmName.SHA256, _signatureFormat)));
    }

    public void Dispose() => _key.Dispose();
}

/// <summary>In-memory trusted store; содержимое задаётся тестом через <see cref="Trust"/>.</summary>
internal sealed class InMemoryTrustedDeviceStore : ITrustedDeviceStore
{
    private static readonly DateTimeOffset PairedAt = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);

    private readonly Dictionary<string, PairedDevice> _devices = new(StringComparer.Ordinal);

    public void Trust(FakePairingKeyStore peer, DeviceRole localRole) =>
        _devices[peer.FingerprintSha256] = new PairedDevice(peer.Identity, localRole, PairedAt);

    public void RemoveFingerprint(string fingerprintSha256) => _devices.Remove(fingerprintSha256);

    public Task<Result<PairedDevice?>> FindAsync(string fingerprintSha256, CancellationToken cancellationToken) =>
        Task.FromResult(_devices.TryGetValue(fingerprintSha256, out var device)
            ? Result<PairedDevice?>.Success(device)
            : Result<PairedDevice?>.Failure(new Error("pairing.not_found", "Доверенное устройство не найдено.")));

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
