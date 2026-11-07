namespace BabyMonitor.Application.Pairing;

/// <summary>Ответ Parent на приглашение Child: публичная identity, эфемерный ключ и подпись.</summary>
public sealed record PairingResponse
{
    /// <summary>Создаёт ответ Parent.</summary>
    /// <param name="parentIdentity">Публичная identity Parent.</param>
    /// <param name="parentEphemeralPublicKey">SPKI эфемерного ECDH-ключа Parent.</param>
    /// <param name="parentSignature">Подпись Parent над транскриптом ответа.</param>
    /// <exception cref="ArgumentException">Некорректные поля.</exception>
    public PairingResponse(
        DeviceIdentityPublicKey parentIdentity,
        byte[] parentEphemeralPublicKey,
        byte[] parentSignature)
    {
        ArgumentNullException.ThrowIfNull(parentIdentity);
        ArgumentNullException.ThrowIfNull(parentEphemeralPublicKey);
        ArgumentNullException.ThrowIfNull(parentSignature);
        if (parentEphemeralPublicKey.Length == 0 || parentSignature.Length == 0)
        {
            throw new ArgumentException("Эфемерный ключ и подпись обязательны.");
        }

        ParentIdentity = parentIdentity;
        ParentEphemeralPublicKey = (byte[])parentEphemeralPublicKey.Clone();
        ParentSignature = (byte[])parentSignature.Clone();
    }

    /// <summary>Публичная identity Parent.</summary>
    public DeviceIdentityPublicKey ParentIdentity { get; }

    /// <summary>SPKI эфемерного ECDH-ключа Parent.</summary>
    public byte[] ParentEphemeralPublicKey { get; }

    /// <summary>Подпись Parent над транскриптом ответа.</summary>
    public byte[] ParentSignature { get; }
}
