using System.Security.Cryptography;
using BabyMonitor.Application.Abstractions.Security;

namespace BabyMonitor.Platform.Android.Security;

/// <summary>
/// CSPRNG-адаптер pairing entropy: BCL RandomNumberGenerator (криптостойкий, fail-closed).
/// Используется для генерации pairing token и ephemeral ключей на платформе.
/// </summary>
public sealed class AndroidSecureEntropy : IPairingEntropy
{
    /// <inheritdoc />
    public byte[] GetBytes(int count)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Количество байт должно быть больше нуля.");
        }

        return RandomNumberGenerator.GetBytes(count);
    }
}
