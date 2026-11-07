namespace BabyMonitor.Application.Pairing;

/// <summary>Ответ Parent на приглашение Child: identity, эфемерный ключ и подпись (копии).</summary>
public sealed class PairingResponse
{
    private readonly byte[] _parentEphemeralPublicKey;
    private readonly byte[] _parentSignature;

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
        _parentEphemeralPublicKey = (byte[])parentEphemeralPublicKey.Clone();
        _parentSignature = (byte[])parentSignature.Clone();
    }

    /// <summary>Публичная identity Parent.</summary>
    public DeviceIdentityPublicKey ParentIdentity { get; }

    /// <summary>SPKI эфемерного ECDH-ключа Parent (копия).</summary>
    public byte[] ParentEphemeralPublicKey => (byte[])_parentEphemeralPublicKey.Clone();

    /// <summary>Подпись Parent над транскриптом ответа (копия).</summary>
    public byte[] ParentSignature => (byte[])_parentSignature.Clone();
}
