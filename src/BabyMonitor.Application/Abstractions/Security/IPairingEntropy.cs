namespace BabyMonitor.Application.Abstractions.Security;

/// <summary>Источник криптостойкой случайности; в тестах подменяется детерминированным fake.</summary>
public interface IPairingEntropy
{
    /// <summary>Возвращает криптостойкие случайные байты.</summary>
    /// <param name="count">Количество байт, больше нуля.</param>
    /// <returns>Новый массив случайных байт.</returns>
    byte[] GetBytes(int count);
}
