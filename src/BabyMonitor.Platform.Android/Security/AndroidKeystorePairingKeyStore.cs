using Android.Security.Keystore;
using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using Java.Security;
using Java.Security.Spec;

namespace BabyMonitor.Platform.Android.Security;

/// <summary>
/// Android Keystore adapter долгосрочной identity: ECDSA P-256, private key non-exportable.
/// Наружу отдаются только SubjectPublicKeyInfo и подпись; секреты не логируются.
/// </summary>
public sealed class AndroidKeystorePairingKeyStore : IPairingKeyStore
{
    private const string AndroidKeyStoreProvider = "AndroidKeyStore";
    private const string KeyAlias = "babymonitor_identity_v1";
    private const string SignatureAlgorithm = "SHA256withECDSA";
    private const string CurveName = "secp256r1";

    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public async Task<Result<DeviceIdentityPublicKey>> GetOrCreateIdentityAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var keyStore = LoadKeyStore();
            if (keyStore is null)
            {
                return KeystoreFailure();
            }

            using (keyStore)
            {
                if (!keyStore.ContainsAlias(KeyAlias))
                {
                    GenerateIdentity();
                }

                return ReadIdentity(keyStore);
            }
        }
        catch (Exception)
        {
            return KeystoreFailure();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<Result<byte[]>> SignAsync(byte[] data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        cancellationToken.ThrowIfCancellationRequested();
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var keyStore = LoadKeyStore();
            if (keyStore is null)
            {
                return Result<byte[]>.Failure(KeystoreError());
            }

            using (keyStore)
            {
                if (keyStore.GetKey(KeyAlias, null) is not IPrivateKey privateKey)
                {
                    return Result<byte[]>.Failure(new Error(
                        PairingErrorCodes.KeystoreUnavailable,
                        "Android Keystore не содержит identity-ключа."));
                }

                var signature = Signature.GetInstance(SignatureAlgorithm);
                if (signature is null)
                {
                    return Result<byte[]>.Failure(KeystoreError());
                }

                using (signature)
                {
                    signature.InitSign(privateKey);
                    signature.Update(data);
                    var signed = signature.Sign();
                    return signed is null
                        ? Result<byte[]>.Failure(KeystoreError())
                        : Result<byte[]>.Success(signed);
                }
            }
        }
        catch (Exception)
        {
            return Result<byte[]>.Failure(KeystoreError());
        }
        finally
        {
            _gate.Release();
        }
    }

    private static KeyStore? LoadKeyStore()
    {
        var keyStore = KeyStore.GetInstance(AndroidKeyStoreProvider);
        if (keyStore is null)
        {
            return null;
        }

        keyStore.Load(null);
        return keyStore;
    }

    private static Result<DeviceIdentityPublicKey> ReadIdentity(KeyStore keyStore)
    {
        if (keyStore.GetCertificate(KeyAlias) is not Java.Security.Cert.X509Certificate certificate)
        {
            return Result<DeviceIdentityPublicKey>.Failure(new Error(
                PairingErrorCodes.KeystoreUnavailable,
                "Android Keystore не содержит сертификата identity."));
        }

        var encoded = certificate.PublicKey?.GetEncoded();
        if (encoded is null)
        {
            return Result<DeviceIdentityPublicKey>.Failure(new Error(
                PairingErrorCodes.KeystoreUnavailable,
                "Android Keystore вернул пустой публичный ключ."));
        }

        try
        {
            return Result<DeviceIdentityPublicKey>.Success(
                DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(encoded));
        }
        catch (ArgumentException)
        {
            return Result<DeviceIdentityPublicKey>.Failure(new Error(
                PairingErrorCodes.KeystoreUnavailable,
                "Android Keystore вернул неподдерживаемый публичный ключ."));
        }
    }

    private static void GenerateIdentity()
    {
        var generator = KeyPairGenerator.GetInstance(KeyProperties.KeyAlgorithmEc, AndroidKeyStoreProvider)
            ?? throw new InvalidOperationException("KeyPairGenerator недоступен.");
        var builder = new KeyGenParameterSpec.Builder(
            KeyAlias,
            KeyStorePurpose.Sign | KeyStorePurpose.Verify);
        builder.SetAlgorithmParameterSpec(new ECGenParameterSpec(CurveName));
        builder.SetDigests(KeyProperties.DigestSha256);
        var spec = builder.Build();
        if (spec is null)
        {
            throw new InvalidOperationException("KeyGenParameterSpec не создан.");
        }

        generator.Initialize(spec);
        generator.GenerateKeyPair();
    }

    private static Result<DeviceIdentityPublicKey> KeystoreFailure() =>
        Result<DeviceIdentityPublicKey>.Failure(KeystoreError());

    private static Error KeystoreError() =>
        new(PairingErrorCodes.KeystoreUnavailable, "Android Keystore недоступен: не удалось получить identity устройства.");
}
