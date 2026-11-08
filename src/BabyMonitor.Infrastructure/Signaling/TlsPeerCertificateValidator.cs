using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Проверка структуры TLS-сертификата peer до app-layer identity assertion. Сертификат peer
/// эфемерный и self-signed, поэтому цепочка/CA/hostname не используются: доверие к identity
/// устанавливается pinning-ом по trusted store и подписанным assertion-ом (ADR-0007). Валидатор
/// принимает только self-signed ECDSA P-256 сертификат нужного назначения (serverAuth/clientAuth)
/// в пределах срока действия — это отсекает чужие типы сертификатов до дорогих криптоопераций.
/// </summary>
internal static class TlsPeerCertificateValidator
{
    internal const string ServerAuthenticationOid = "1.3.6.1.5.5.7.3.1";
    internal const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private const int P256KeySizeBits = 256;

    /// <summary>Проверяет структуру сертификата peer.</summary>
    /// <param name="certificate">Сертификат peer из TLS handshake.</param>
    /// <param name="requiredEnhancedKeyUsageOid">Требуемое назначение: serverAuth или clientAuth.</param>
    /// <param name="timeProvider">Источник времени для проверки срока действия.</param>
    /// <returns>true, если структура допустима; иначе false.</returns>
    public static bool IsAcceptable(
        X509Certificate? certificate,
        string requiredEnhancedKeyUsageOid,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (certificate is not X509Certificate2 peerCertificate)
        {
            return false;
        }

        if (!string.Equals(peerCertificate.Subject, peerCertificate.Issuer, StringComparison.Ordinal))
        {
            return false;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (peerCertificate.NotBefore.ToUniversalTime() > now || peerCertificate.NotAfter.ToUniversalTime() < now)
        {
            return false;
        }

        var basicConstraints = peerCertificate.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
        if (basicConstraints is { CertificateAuthority: true })
        {
            return false;
        }

        var enhancedKeyUsage = peerCertificate.Extensions.OfType<X509EnhancedKeyUsageExtension>().FirstOrDefault();
        if (enhancedKeyUsage is null)
        {
            return false;
        }

        var hasRequiredUsage = enhancedKeyUsage.EnhancedKeyUsages
            .Cast<Oid>()
            .Any(oid => string.Equals(oid.Value, requiredEnhancedKeyUsageOid, StringComparison.Ordinal));
        if (!hasRequiredUsage)
        {
            return false;
        }

        using var publicKey = peerCertificate.GetECDsaPublicKey();
        return publicKey is not null && publicKey.KeySize == P256KeySizeBits;
    }

    /// <summary>SHA-256 от DER SubjectPublicKeyInfo сертификата peer.</summary>
    public static byte[] ComputeSubjectPublicKeyInfoSha256(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        return SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
    }
}
