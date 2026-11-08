using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BabyMonitor.Infrastructure.Signaling;

/// <summary>
/// Payload кадра hello: версия протокола, nonce инициатора, SHA-256 SPKI TLS-сертификатов обеих
/// сторон, fingerprint identity инициатора и identity assertion (ECDSA P-256). Фиксированный
/// бинарный формат версии 1; поля проверяются по длине до использования.
/// </summary>
internal sealed class SignalingHello
{
    internal SignalingHello(
        ushort protocolVersion,
        byte[] initiatorNonce,
        byte[] initiatorCertificateSpkiHash,
        byte[] responderCertificateSpkiHash,
        byte[] initiatorIdentityFingerprint,
        byte[] assertion)
    {
        ProtocolVersion = protocolVersion;
        InitiatorNonce = initiatorNonce;
        InitiatorCertificateSpkiHash = initiatorCertificateSpkiHash;
        ResponderCertificateSpkiHash = responderCertificateSpkiHash;
        InitiatorIdentityFingerprint = initiatorIdentityFingerprint;
        Assertion = assertion;
    }

    internal ushort ProtocolVersion { get; }

    internal byte[] InitiatorNonce { get; }

    internal byte[] InitiatorCertificateSpkiHash { get; }

    internal byte[] ResponderCertificateSpkiHash { get; }

    internal byte[] InitiatorIdentityFingerprint { get; }

    internal byte[] Assertion { get; }
}

/// <summary>
/// Payload кадра hello_ack: версия протокола, эхо nonce инициатора, nonce слушателя, SHA-256 SPKI
/// TLS-сертификатов обеих сторон, fingerprint identity слушателя и его assertion.
/// </summary>
internal sealed class SignalingHelloAck
{
    internal SignalingHelloAck(
        ushort protocolVersion,
        byte[] initiatorNonce,
        byte[] responderNonce,
        byte[] initiatorCertificateSpkiHash,
        byte[] responderCertificateSpkiHash,
        byte[] responderIdentityFingerprint,
        byte[] assertion)
    {
        ProtocolVersion = protocolVersion;
        InitiatorNonce = initiatorNonce;
        ResponderNonce = responderNonce;
        InitiatorCertificateSpkiHash = initiatorCertificateSpkiHash;
        ResponderCertificateSpkiHash = responderCertificateSpkiHash;
        ResponderIdentityFingerprint = responderIdentityFingerprint;
        Assertion = assertion;
    }

    internal ushort ProtocolVersion { get; }

    internal byte[] InitiatorNonce { get; }

    internal byte[] ResponderNonce { get; }

    internal byte[] InitiatorCertificateSpkiHash { get; }

    internal byte[] ResponderCertificateSpkiHash { get; }

    internal byte[] ResponderIdentityFingerprint { get; }

    internal byte[] Assertion { get; }
}

/// <summary>
/// Бинарный кодек hello/hello_ack. Некорректная длина, нулевые nonce и несогласованная длина
/// assertion трактуются как malformed и не приводят к чтению за границами payload.
/// </summary>
internal static class SignalingHelloCodec
{
    private const int HashLength = 32;
    private const int VersionLength = 2;
    private const int AssertionLengthField = 2;
    private const int MinAssertionLength = 8;

    internal static byte[] EncodeHello(SignalingHello hello)
    {
        ArgumentNullException.ThrowIfNull(hello);
        ValidateHashLength(hello.InitiatorNonce, nameof(hello));
        ValidateHashLength(hello.InitiatorCertificateSpkiHash, nameof(hello));
        ValidateHashLength(hello.ResponderCertificateSpkiHash, nameof(hello));
        ValidateHashLength(hello.InitiatorIdentityFingerprint, nameof(hello));
        ValidateAssertion(hello.Assertion, nameof(hello));

        var payload = new byte[VersionLength + (HashLength * 4) + AssertionLengthField + hello.Assertion.Length];
        var offset = 0;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), hello.ProtocolVersion);
        offset += VersionLength;
        offset = Copy(hello.InitiatorNonce, payload, offset);
        offset = Copy(hello.InitiatorCertificateSpkiHash, payload, offset);
        offset = Copy(hello.ResponderCertificateSpkiHash, payload, offset);
        offset = Copy(hello.InitiatorIdentityFingerprint, payload, offset);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), checked((ushort)hello.Assertion.Length));
        offset += AssertionLengthField;
        Copy(hello.Assertion, payload, offset);
        return payload;
    }

    internal static SignalingHello DecodeHello(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var protocolVersion = ReadProtocolVersion(payload, ref offset);
        var initiatorNonce = ReadHash(payload, ref offset, "initiator nonce");
        var initiatorCertificateSpkiHash = ReadHash(payload, ref offset, "initiator certificate hash");
        var responderCertificateSpkiHash = ReadHash(payload, ref offset, "responder certificate hash");
        var initiatorIdentityFingerprint = ReadHash(payload, ref offset, "initiator identity fingerprint");
        var assertion = ReadAssertion(payload, ref offset);

        if (!NonceIsNonZero(initiatorNonce))
        {
            throw new SignalingProtocolException("hello содержит нулевой nonce.");
        }

        return new SignalingHello(
            protocolVersion,
            initiatorNonce,
            initiatorCertificateSpkiHash,
            responderCertificateSpkiHash,
            initiatorIdentityFingerprint,
            assertion);
    }

    internal static byte[] EncodeHelloAck(SignalingHelloAck helloAck)
    {
        ArgumentNullException.ThrowIfNull(helloAck);
        ValidateHashLength(helloAck.InitiatorNonce, nameof(helloAck));
        ValidateHashLength(helloAck.ResponderNonce, nameof(helloAck));
        ValidateHashLength(helloAck.InitiatorCertificateSpkiHash, nameof(helloAck));
        ValidateHashLength(helloAck.ResponderCertificateSpkiHash, nameof(helloAck));
        ValidateHashLength(helloAck.ResponderIdentityFingerprint, nameof(helloAck));
        ValidateAssertion(helloAck.Assertion, nameof(helloAck));

        var payload = new byte[VersionLength + (HashLength * 5) + AssertionLengthField + helloAck.Assertion.Length];
        var offset = 0;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), helloAck.ProtocolVersion);
        offset += VersionLength;
        offset = Copy(helloAck.InitiatorNonce, payload, offset);
        offset = Copy(helloAck.ResponderNonce, payload, offset);
        offset = Copy(helloAck.InitiatorCertificateSpkiHash, payload, offset);
        offset = Copy(helloAck.ResponderCertificateSpkiHash, payload, offset);
        offset = Copy(helloAck.ResponderIdentityFingerprint, payload, offset);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(offset), checked((ushort)helloAck.Assertion.Length));
        offset += AssertionLengthField;
        Copy(helloAck.Assertion, payload, offset);
        return payload;
    }

    internal static SignalingHelloAck DecodeHelloAck(ReadOnlySpan<byte> payload)
    {
        var offset = 0;
        var protocolVersion = ReadProtocolVersion(payload, ref offset);
        var initiatorNonce = ReadHash(payload, ref offset, "initiator nonce");
        var responderNonce = ReadHash(payload, ref offset, "responder nonce");
        var initiatorCertificateSpkiHash = ReadHash(payload, ref offset, "initiator certificate hash");
        var responderCertificateSpkiHash = ReadHash(payload, ref offset, "responder certificate hash");
        var responderIdentityFingerprint = ReadHash(payload, ref offset, "responder identity fingerprint");
        var assertion = ReadAssertion(payload, ref offset);

        if (!NonceIsNonZero(initiatorNonce) || !NonceIsNonZero(responderNonce))
        {
            throw new SignalingProtocolException("hello_ack содержит нулевой nonce.");
        }

        return new SignalingHelloAck(
            protocolVersion,
            initiatorNonce,
            responderNonce,
            initiatorCertificateSpkiHash,
            responderCertificateSpkiHash,
            responderIdentityFingerprint,
            assertion);
    }

    private static ushort ReadProtocolVersion(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length < VersionLength)
        {
            throw new SignalingProtocolException("Кадр hello короче заголовка.");
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(payload);
        offset += VersionLength;
        return version;
    }

    private static byte[] ReadHash(ReadOnlySpan<byte> payload, ref int offset, string fieldName)
    {
        if (payload.Length - offset < HashLength)
        {
            throw new SignalingProtocolException("Кадр hello обрезан: не хватает поля " + fieldName + ".");
        }

        var value = payload.Slice(offset, HashLength).ToArray();
        offset += HashLength;
        return value;
    }

    private static byte[] ReadAssertion(ReadOnlySpan<byte> payload, ref int offset)
    {
        if (payload.Length - offset < AssertionLengthField)
        {
            throw new SignalingProtocolException("Кадр hello обрезан: нет длины assertion.");
        }

        var assertionLength = BinaryPrimitives.ReadUInt16BigEndian(payload[offset..]);
        offset += AssertionLengthField;
        if (assertionLength < MinAssertionLength || payload.Length - offset != assertionLength)
        {
            throw new SignalingProtocolException("Некорректная длина identity assertion.");
        }

        var assertion = payload.Slice(offset, assertionLength).ToArray();
        return assertion;
    }

    private static int Copy(byte[] source, byte[] destination, int offset)
    {
        source.CopyTo(destination, offset);
        return offset + source.Length;
    }

    private static bool NonceIsNonZero(byte[] nonce) => !nonce.AsSpan().SequenceEqual(new byte[HashLength]);

    private static void ValidateHashLength(byte[] value, string parameterName)
    {
        if (value is null || value.Length != HashLength)
        {
            throw new ArgumentException("Ожидались 32 байта (SHA-256/nonce).", parameterName);
        }
    }

    private static void ValidateAssertion(byte[] assertion, string parameterName)
    {
        if (assertion is null || assertion.Length < MinAssertionLength || assertion.Length > ushort.MaxValue)
        {
            throw new ArgumentException("Некорректная длина identity assertion.", parameterName);
        }
    }
}
