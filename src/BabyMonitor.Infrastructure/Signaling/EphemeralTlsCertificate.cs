using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Эфемерный self-signed TLS-сертификат (ECDSA P-256), создаваемый на одно signaling-соединение.
/// Долгосрочная identity в TLS не используется: private key в Android Keystore non-exportable и не
/// может быть передан в SslStream, поэтому по ADR-0007 (fallback, вариант A) TLS обеспечивает
/// шифрование канала, а identity peer доказывается app-layer assertion-ом, подписанным Keystore.
/// Сертификат не содержит identity, приватных данных и не сохраняется.
/// </summary>
internal sealed class EphemeralTlsCertificate : IDisposable
{
    private readonly X509Certificate2 _certificate;
    private bool _disposed;

    private EphemeralTlsCertificate(X509Certificate2 certificate)
    {
        _certificate = certificate;
        SubjectPublicKeyInfoSha256 = SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>SHA-256 от DER SubjectPublicKeyInfo сертификата (channel binding для assertion).</summary>
    public byte[] SubjectPublicKeyInfoSha256 { get; }

    /// <summary>Сертификат с private key для SslStream.</summary>
    public X509Certificate2 Certificate => _certificate;

    /// <summary>Создаёт эфемерный сертификат, действительный на сутки.</summary>
    /// <param name="timeProvider">Источник времени для периода действия.</param>
    /// <returns>Сертификат и его SPKI hash.</returns>
    public static EphemeralTlsCertificate Create(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=babymonitor-ephemeral", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            new OidCollection
            {
                new(TlsPeerCertificateValidator.ServerAuthenticationOid),
                new(TlsPeerCertificateValidator.ClientAuthenticationOid)
            },
            critical: false));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: false));

        var now = timeProvider.GetUtcNow();
        var certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddHours(24));
        try
        {
            // Windows Schannel (SslStream) не принимает сертификаты с ephemeral CNG-ключами:
            // экспорт/импорт PFX создаёт платформенный key container. На Unix ключ остаётся в памяти.
            var keyStorageFlags = OperatingSystem.IsWindows()
                ? X509KeyStorageFlags.DefaultKeySet
                : X509KeyStorageFlags.EphemeralKeySet;
            var pfx = certificate.Export(X509ContentType.Pfx);
            try
            {
                var imported = X509CertificateLoader.LoadPkcs12(pfx, password: null, keyStorageFlags);
                return new EphemeralTlsCertificate(imported);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(pfx);
            }
        }
        finally
        {
            certificate.Dispose();
        }
    }

    /// <summary>Создаёт контекст сертификата для SslStream без сетевого построения цепочки.</summary>
    public SslStreamCertificateContext CreateCertificateContext() =>
        SslStreamCertificateContext.Create(_certificate, additionalCertificates: null, offline: true);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _certificate.Dispose();
    }
}
