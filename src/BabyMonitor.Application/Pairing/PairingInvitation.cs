namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Приглашение Child на сопряжение. Token — секрет: не логируется и не попадает в Error.
/// </summary>
public sealed record PairingInvitation
{
    /// <summary>Создаёт приглашение.</summary>
    /// <param name="protocolVersion">Версия протокола.</param>
    /// <param name="token">Одноразовый token (base64url).</param>
    /// <param name="expiresAt">Момент истечения.</param>
    /// <param name="childIdentity">Публичная identity Child.</param>
    /// <param name="childEphemeralPublicKey">SPKI эфемерного ECDH-ключа Child.</param>
    /// <param name="childSignature">Подпись Child над транскриптом приглашения.</param>
    /// <exception cref="ArgumentException">Некорректные поля.</exception>
    public PairingInvitation(
        int protocolVersion,
        string token,
        DateTimeOffset expiresAt,
        DeviceIdentityPublicKey childIdentity,
        byte[] childEphemeralPublicKey,
        byte[] childSignature)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        ArgumentNullException.ThrowIfNull(childIdentity);
        ArgumentNullException.ThrowIfNull(childEphemeralPublicKey);
        ArgumentNullException.ThrowIfNull(childSignature);
        if (childEphemeralPublicKey.Length == 0 || childSignature.Length == 0)
        {
            throw new ArgumentException("Эфемерный ключ и подпись обязательны.");
        }

        ProtocolVersion = protocolVersion;
        Token = token;
        ExpiresAt = expiresAt;
        ChildIdentity = childIdentity;
        ChildEphemeralPublicKey = (byte[])childEphemeralPublicKey.Clone();
        ChildSignature = (byte[])childSignature.Clone();
    }

    /// <summary>Версия протокола.</summary>
    public int ProtocolVersion { get; }

    /// <summary>Одноразовый token (секрет).</summary>
    public string Token { get; }

    /// <summary>Момент истечения приглашения.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Публичная identity Child.</summary>
    public DeviceIdentityPublicKey ChildIdentity { get; }

    /// <summary>SPKI эфемерного ECDH-ключа Child.</summary>
    public byte[] ChildEphemeralPublicKey { get; }

    /// <summary>Подпись Child над транскриптом приглашения.</summary>
    public byte[] ChildSignature { get; }
}
