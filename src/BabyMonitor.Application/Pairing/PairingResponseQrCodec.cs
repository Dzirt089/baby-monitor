using System.Text;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Кодирование/разбор QR payload ответа Parent для офлайн-обмена (Child ↔ Parent без сети).
/// Формат — версионированный бинарный payload в base64url (без JSON): SPKI identity Parent,
/// эфемерный SPKI Parent и подпись Parent, каждое поле с little-endian длиной int32.
/// Приватные ключи и общий секрет в QR отсутствуют: payload безопасен для показа на экране,
/// но любой сбой разбора даёт нейтральную ошибку invalid_qr без деталей.
/// </summary>
public static class PairingResponseQrCodec
{
    private const string VersionPrefix = "v1.";
    private const int MaxPayloadBytes = 8 * 1024;
    private const int MaxFieldBytes = 4 * 1024;

    /// <summary>Кодирует ответ Parent в QR-payload.</summary>
    /// <param name="response">Ответ Parent.</param>
    /// <returns>Строка с префиксом <see cref="PairingConstants.ResponseQrPrefix"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="response"/> равен <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">Поле ответа или payload превышает допустимый размер.</exception>
    public static string Encode(PairingResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var parentIdentity = response.ParentIdentity.SubjectPublicKeyInfo.ToArray();
        var parentEphemeralPublicKey = response.ParentEphemeralPublicKey;
        var parentSignature = response.ParentSignature;
        if (parentIdentity.Length > MaxFieldBytes
            || parentEphemeralPublicKey.Length > MaxFieldBytes
            || parentSignature.Length > MaxFieldBytes)
        {
            throw new ArgumentException("Поле ответа Parent превышает допустимый размер QR.", nameof(response));
        }

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        WriteBytes(writer, parentIdentity);
        WriteBytes(writer, parentEphemeralPublicKey);
        WriteBytes(writer, parentSignature);
        writer.Flush();
        if (stream.Length > MaxPayloadBytes)
        {
            throw new ArgumentException("Ответ Parent превышает допустимый размер QR.", nameof(response));
        }

        return PairingConstants.ResponseQrPrefix + VersionPrefix + PairingCrypto.ToBase64Url(stream.ToArray());
    }

    /// <summary>
    /// Разбирает QR-payload ответа Parent. Проверяет префикс, версию, base64url, размеры полей,
    /// отсутствие лишних байт и валидность SPKI; подпись проверяет ChildPairingSession.
    /// </summary>
    /// <param name="payload">QR-payload ответа Parent (может быть <see langword="null"/> при вводе вручную).</param>
    /// <returns>Ответ Parent либо invalid_qr; payload и его части в ошибку не попадают.</returns>
    public static Result<PairingResponse> Decode(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload)
            || !payload.StartsWith(PairingConstants.ResponseQrPrefix, StringComparison.Ordinal))
        {
            return Failure();
        }

        var body = payload[PairingConstants.ResponseQrPrefix.Length..];
        if (!body.StartsWith(VersionPrefix, StringComparison.Ordinal)
            || !PairingCrypto.TryFromBase64Url(body[VersionPrefix.Length..], out var bytes)
            || bytes.Length is 0 or > MaxPayloadBytes)
        {
            return Failure();
        }

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var parentIdentity = ReadBytes(reader);
            var parentEphemeralPublicKey = ReadBytes(reader);
            var parentSignature = ReadBytes(reader);

            if (stream.Position != stream.Length
                || parentIdentity.Length == 0
                || parentEphemeralPublicKey.Length == 0
                || parentSignature.Length == 0
                || !PairingCrypto.TryValidateIdentityPublicKey(parentIdentity)
                || !PairingCrypto.TryValidateEphemeralPublicKey(parentEphemeralPublicKey))
            {
                return Failure();
            }

            return Result<PairingResponse>.Success(new PairingResponse(
                DeviceIdentityPublicKey.FromSubjectPublicKeyInfo(parentIdentity),
                parentEphemeralPublicKey,
                parentSignature));
        }
        catch (Exception exception) when (exception is EndOfStreamException or IOException or ArgumentException)
        {
            return Failure();
        }
    }

    private static Result<PairingResponse> Failure() => Result<PairingResponse>.Failure(
        new Error(PairingErrorCodes.InvalidQr, "QR-код ответа родителя повреждён или не поддерживается."));

    private static void WriteBytes(BinaryWriter writer, byte[] bytes)
    {
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static byte[] ReadBytes(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length is < 0 or > MaxFieldBytes)
        {
            throw new ArgumentException("Некорректная длина поля QR-ответа.");
        }

        var data = reader.ReadBytes(length);
        if (data.Length != length)
        {
            throw new EndOfStreamException();
        }

        return data;
    }
}
