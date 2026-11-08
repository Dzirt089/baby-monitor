using System.Buffers.Binary;
using System.Security.Cryptography;
using BabyMonitor.Application.Pairing;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Identity assertion поверх TLS: подпись долгосрочной identity (ECDSA P-256) над транскриптом,
/// который связывает версию протокола, nonce инициатора/слушателя, SHA-256 SPKI обоих эфемерных
/// TLS-сертификатов и fingerprint подписанта. Такая привязка (channel binding) не позволяет
/// атакующему переиспользовать assertion в другом TLS-соединении/транскрипте (ADR-0007, вариант A).
/// </summary>
internal static class SignalingAssertions
{
    private const int HashLength = 32;
    private const int VersionLength = 2;

    /// <summary>Домен разделения для hello инициатора.</summary>
    internal static ReadOnlySpan<byte> InitiatorDomain => "BabyMonitor.Signaling.InitiatorHello.v1:"u8;

    /// <summary>Домен разделения для hello_ack слушателя.</summary>
    internal static ReadOnlySpan<byte> ResponderDomain => "BabyMonitor.Signaling.ResponderHelloAck.v1:"u8;

    /// <summary>Строит транскрипт hello инициатора.</summary>
    internal static byte[] BuildInitiatorTranscript(
        ushort protocolVersion,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> initiatorCertificateSpkiHash,
        ReadOnlySpan<byte> responderCertificateSpkiHash,
        ReadOnlySpan<byte> initiatorIdentityFingerprint)
    {
        ValidatePart(initiatorNonce, nameof(initiatorNonce));
        ValidatePart(initiatorCertificateSpkiHash, nameof(initiatorCertificateSpkiHash));
        ValidatePart(responderCertificateSpkiHash, nameof(responderCertificateSpkiHash));
        ValidatePart(initiatorIdentityFingerprint, nameof(initiatorIdentityFingerprint));

        var transcript = new byte[InitiatorDomain.Length + VersionLength + (HashLength * 4)];
        var offset = CopyDomain(transcript, InitiatorDomain);
        BinaryPrimitives.WriteUInt16BigEndian(transcript.AsSpan(offset), protocolVersion);
        offset += VersionLength;
        initiatorNonce.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        initiatorCertificateSpkiHash.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        responderCertificateSpkiHash.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        initiatorIdentityFingerprint.CopyTo(transcript.AsSpan(offset));
        return transcript;
    }

    /// <summary>Строит транскрипт hello_ack слушателя (включает оба nonce).</summary>
    internal static byte[] BuildResponderTranscript(
        ushort protocolVersion,
        ReadOnlySpan<byte> initiatorNonce,
        ReadOnlySpan<byte> responderNonce,
        ReadOnlySpan<byte> initiatorCertificateSpkiHash,
        ReadOnlySpan<byte> responderCertificateSpkiHash,
        ReadOnlySpan<byte> responderIdentityFingerprint)
    {
        ValidatePart(initiatorNonce, nameof(initiatorNonce));
        ValidatePart(responderNonce, nameof(responderNonce));
        ValidatePart(initiatorCertificateSpkiHash, nameof(initiatorCertificateSpkiHash));
        ValidatePart(responderCertificateSpkiHash, nameof(responderCertificateSpkiHash));
        ValidatePart(responderIdentityFingerprint, nameof(responderIdentityFingerprint));

        var transcript = new byte[ResponderDomain.Length + VersionLength + (HashLength * 5)];
        var offset = CopyDomain(transcript, ResponderDomain);
        BinaryPrimitives.WriteUInt16BigEndian(transcript.AsSpan(offset), protocolVersion);
        offset += VersionLength;
        initiatorNonce.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        responderNonce.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        initiatorCertificateSpkiHash.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        responderCertificateSpkiHash.CopyTo(transcript.AsSpan(offset));
        offset += HashLength;
        responderIdentityFingerprint.CopyTo(transcript.AsSpan(offset));
        return transcript;
    }

    /// <summary>
    /// Проверяет assertion: fingerprint подписанта совпадает с ожидаемым (pinning) и ECDSA-подпись
    /// валидна для переданного транскрипта. Любая ошибка разбора — отказ, а не исключение.
    /// </summary>
    internal static bool Verify(
        DeviceIdentityPublicKey expectedPeerIdentity,
        ReadOnlySpan<byte> assertedIdentityFingerprint,
        ReadOnlySpan<byte> transcript,
        ReadOnlySpan<byte> signature)
    {
        if (expectedPeerIdentity is null || signature.IsEmpty || transcript.IsEmpty || assertedIdentityFingerprint.Length != HashLength)
        {
            return false;
        }

        var expectedFingerprint = Convert.FromHexString(expectedPeerIdentity.FingerprintSha256);
        if (!CryptographicOperations.FixedTimeEquals(expectedFingerprint, assertedIdentityFingerprint))
        {
            return false;
        }

        return expectedPeerIdentity.VerifySignature(transcript, signature);
    }

    /// <summary>Сырые 32 байта SHA-256 fingerprint из hex-представления identity.</summary>
    internal static byte[] FingerprintToRaw(DeviceIdentityPublicKey identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Convert.FromHexString(identity.FingerprintSha256);
    }

    private static int CopyDomain(Span<byte> destination, ReadOnlySpan<byte> domain)
    {
        domain.CopyTo(destination);
        return domain.Length;
    }

    private static void ValidatePart(ReadOnlySpan<byte> part, string parameterName)
    {
        if (part.Length != HashLength)
        {
            throw new ArgumentException("Ожидались 32 байта (SHA-256/nonce).", parameterName);
        }
    }
}
