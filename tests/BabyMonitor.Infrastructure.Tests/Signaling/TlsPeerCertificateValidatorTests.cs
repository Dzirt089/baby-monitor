using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using BabyMonitor.Infrastructure.Signaling;

namespace BabyMonitor.Infrastructure.Tests.Signaling;

/// <summary>
/// Прямые unit-тесты структурного pre-filter-а TLS-сертификата peer (TlsPeerCertificateValidator):
/// допустим только self-signed ECDSA P-256 нужного назначения в пределах срока действия. Валидатор
/// не принимает решение о доверии — identity утверждается assertion-ом и pinning-ом по trusted store
/// (ADR-0007), поэтому «чужой» структурно валидный сертификат проходит эту проверку.
/// </summary>
public sealed class TlsPeerCertificateValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeProvider FixedTime = new FixedTimeProvider(Now);

    private static readonly string[] ServerEku = [TlsPeerCertificateValidator.ServerAuthenticationOid];
    private static readonly string[] ClientEku = [TlsPeerCertificateValidator.ClientAuthenticationOid];

    [Fact]
    public void Selfsigned_ECDSA_P256_с_serverAuth_допускается()
    {
        using var certificate = CreateEcdsa(ServerEku);

        Assert.True(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Selfsigned_ECDSA_P256_с_clientAuth_допускается()
    {
        using var certificate = CreateEcdsa(ClientEku);

        Assert.True(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ClientAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Сертификат_с_CA_true_отклоняется()
    {
        using var certificate = CreateEcdsa(ServerEku, isCertificateAuthority: true);

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Просроченный_сертификат_отклоняется()
    {
        using var certificate = CreateEcdsa(
            ServerEku,
            notBefore: Now.AddHours(-2),
            notAfter: Now.AddHours(-1));

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Ещё_не_вступивший_в_силу_сертификат_отклоняется()
    {
        using var certificate = CreateEcdsa(
            ServerEku,
            notBefore: Now.AddHours(1),
            notAfter: Now.AddHours(2));

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Сертификат_без_EKU_отклоняется()
    {
        using var certificate = CreateEcdsa([]);

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Сертификат_с_чужим_EKU_отклоняется()
    {
        using var serverOnly = CreateEcdsa(ServerEku);
        using var clientOnly = CreateEcdsa(ClientEku);

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(clientOnly, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
        Assert.False(TlsPeerCertificateValidator.IsAcceptable(serverOnly, TlsPeerCertificateValidator.ClientAuthenticationOid, FixedTime));
    }

    [Fact]
    public void RSA_сертификат_отклоняется()
    {
        using var certificate = CreateRsa(ServerEku);

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Сертификат_с_ключом_не_P256_отклоняется()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var request = new CertificateRequest("CN=babymonitor-p384", ecdsa, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(CreateEku(ServerEku));
        using var certificate = request.CreateSelfSigned(Now.AddMinutes(-5), Now.AddHours(1));

        Assert.False(TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Null_сертификат_отклоняется()
    {
        Assert.False(TlsPeerCertificateValidator.IsAcceptable(null, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Malformed_сертификат_не_принимается_молча()
    {
#pragma warning disable SYSLIB0026 // Намеренно создаём пустой сертификат для malformed-ветки.
        using var malformed = new X509Certificate2();
#pragma warning restore SYSLIB0026

        // Контракт валидатора: malformed не должен молча приниматься; допустимо типизированное
        // исключение при чтении свойств пустого сертификата.
        var exception = Record.Exception(
            () => TlsPeerCertificateValidator.IsAcceptable(malformed, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));

        Assert.IsType<CryptographicException>(exception);
    }

    [Fact]
    public void TimeProvider_обязателен()
    {
        using var certificate = CreateEcdsa(ServerEku);

        Assert.Throws<ArgumentNullException>(
            () => TlsPeerCertificateValidator.IsAcceptable(certificate, TlsPeerCertificateValidator.ServerAuthenticationOid, null!));
    }

    [Fact]
    public void Границы_срока_действия_проверяются_по_TimeProvider()
    {
        // NotAfter == now и NotBefore == now валидны (сравнение строгое); +1 с уже отклоняется.
        using var endingNow = CreateEcdsa(ServerEku, notBefore: Now.AddHours(-1), notAfter: Now);
        using var startsNow = CreateEcdsa(ServerEku, notBefore: Now, notAfter: Now.AddHours(1));
        using var expiredOneSecondAgo = CreateEcdsa(ServerEku, notBefore: Now.AddHours(-1), notAfter: Now.AddSeconds(-1));
        using var startsInOneSecond = CreateEcdsa(ServerEku, notBefore: Now.AddSeconds(1), notAfter: Now.AddHours(1));

        Assert.True(TlsPeerCertificateValidator.IsAcceptable(endingNow, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
        Assert.True(TlsPeerCertificateValidator.IsAcceptable(startsNow, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
        Assert.False(TlsPeerCertificateValidator.IsAcceptable(expiredOneSecondAgo, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
        Assert.False(TlsPeerCertificateValidator.IsAcceptable(startsInOneSecond, TlsPeerCertificateValidator.ServerAuthenticationOid, FixedTime));
    }

    [Fact]
    public void Структурно_валидный_сертификат_чужой_identity_проходит_только_структурный_pre_filter()
    {
        // Валидатор — pre-filter, а не trust decision: произвольная (не paired) identity структурно
        // допустима; доверие устанавливают identity assertion + pinning по trusted store (ADR-0007).
        using var foreignCertificate = CreateEcdsa(ServerEku);

        Assert.True(TlsPeerCertificateValidator.IsAcceptable(
            foreignCertificate,
            TlsPeerCertificateValidator.ServerAuthenticationOid,
            FixedTime));
    }

    private static X509Certificate2 CreateEcdsa(
        string[] enhancedKeyUsageOids,
        bool isCertificateAuthority = false,
        DateTimeOffset? notBefore = null,
        DateTimeOffset? notAfter = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=babymonitor-test", ecdsa, HashAlgorithmName.SHA256);
        if (enhancedKeyUsageOids.Length > 0)
        {
            request.CertificateExtensions.Add(CreateEku(enhancedKeyUsageOids));
        }

        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(isCertificateAuthority, false, 0, critical: true));
        return request.CreateSelfSigned(notBefore ?? Now.AddMinutes(-5), notAfter ?? Now.AddHours(1));
    }

    private static X509Certificate2 CreateRsa(string[] enhancedKeyUsageOids)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=babymonitor-rsa", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(CreateEku(enhancedKeyUsageOids));
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        return request.CreateSelfSigned(Now.AddMinutes(-5), Now.AddHours(1));
    }

    private static X509EnhancedKeyUsageExtension CreateEku(string[] enhancedKeyUsageOids)
    {
        var oids = new OidCollection();
        foreach (var oid in enhancedKeyUsageOids)
        {
            oids.Add(new Oid(oid));
        }

        return new X509EnhancedKeyUsageExtension(oids, critical: false);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
