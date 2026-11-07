using System.Text;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Кодирование/разбор QR payload приглашения. Формат — версионированный бинарный payload в
/// base64url (без JSON): version, token, expiresAt, identity SPKI, ephemeral SPKI, signature.
/// Payload содержит token (секрет); private keys и shared secrets в QR отсутствуют.
/// </summary>
public static class PairingQrCodec
{
    private const string VersionPrefix = "v1.";

    /// <summary>Кодирует приглашение в QR-payload.</summary>
    /// <param name="invitation">Приглашение Child.</param>
    /// <returns>Строка с префиксом QrPrefix.</returns>
    public static string Encode(PairingInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(invitation.ProtocolVersion);
        WriteBytes(writer, Encoding.UTF8.GetBytes(invitation.Token));
        writer.Write(invitation.ExpiresAt.ToUnixTimeSeconds());
        WriteBytes(writer, invitation.ChildIdentity.SubjectPublicKeyInfo.ToArray());
        WriteBytes(writer, invitation.ChildEphemeralPublicKey);
        WriteBytes(writer, invitation.ChildSignature);
        writer.Flush();

        return PairingConstants.QrPrefix + VersionPrefix + PairingCrypto.ToBase64Url(stream.ToArray());
    }

    /// <summary>Разбирает QR-payload (без проверки подписи/срока; это делает ParentPairingSession).</summary>
    /// <param name="payload">QR-payload.</param>
    /// <returns>Приглашение либо invalid_qr.</returns>
    public static Result<PairingInvitation> Decode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || !payload.StartsWith(PairingConstants.QrPrefix, StringComparison.Ordinal))
        {
            return Failure();
        }

        var body = payload[PairingConstants.QrPrefix.Length..];
        if (!body.StartsWith(VersionPrefix, StringComparison.Ordinal)
            || !PairingCrypto.TryFromBase64Url(body[VersionPrefix.Length..], out var bytes))
        {
            return Failure();
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var protocolVersion = reader.ReadInt32();
            var tokenBytes = ReadBytes(reader);
            var expiresAtUnixSeconds = reader.ReadInt64();
            var childIdentity = ReadBytes(reader);
            var childEphemeralPublicKey = ReadBytes(reader);
            var childSignature = ReadBytes(reader);

            if (stream.Position != stream.Length)
            {
                return Failure();
            }

            var token = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(tokenBytes);
            if (string.IsNullOrWhiteSpace(token)
                || protocolVersion <= 0
                || expiresAtUnixSeconds <= 0
                || childIdentity.Length == 0
                || childEphemeralPublicKey.Length == 0
                || childSignature.Length == 0)
            {
                return Failure();
            }

            var invitation = new PairingInvitation(
                protocolVersion,
                token,
                DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixSeconds),
                DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(childIdentity),
                childEphemeralPublicKey,
                childSignature);
            return Result<PairingInvitation>.Success(invitation);
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException or DecoderFallbackException)
        {
            return Failure();
        }
    }

    private static Result<PairingInvitation> Failure() =>
        Result<PairingInvitation>.Failure(new Error(PairingErrorCodes.InvalidQr, "QR-код сопряжения повреждён или не поддерживается."));

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > 64 * 1024)
        {
            throw new ArgumentException("Некорректная длина поля QR.");
        }

        return reader.ReadBytes(length);
    }
}
