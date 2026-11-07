namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Приглашение Child на сопряжение. Token — секрет: не логируется и не попадает в Error.
/// Массивы отдаются копиями, чтобы подписанные/проверенные данные нельзя было мутировать.
/// </summary>
public sealed class PairingInvitation
{
    private readonly byte[] _childEphemeralPublicKey;
    private readonly byte[] _childSignature;

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
        _childEphemeralPublicKey = (byte[])childEphemeralPublicKey.Clone();
        _childSignature = (byte[])childSignature.Clone();
    }

    /// <summary>Версия протокола.</summary>
    public int ProtocolVersion { get; }

    /// <summary>Одноразовый token (секрет).</summary>
    public string Token { get; }

    /// <summary>Момент истечения приглашения.</summary>
    public DateTimeOffset ExpiresAt { get; }

    /// <summary>Публичная identity Child.</summary>
    public DeviceIdentityPublicKey ChildIdentity { get; }

    /// <summary>SPKI эфемерного ECDH-ключа Child (копия).</summary>
    public byte[] ChildEphemeralPublicKey => (byte[])_childEphemeralPublicKey.Clone();

    /// <summary>Подпись Child над транскриптом приглашения (копия).</summary>
    public byte[] ChildSignature => (byte[])_childSignature.Clone();
}
