namespace BabyMonitor.Application.Updates;

/// <summary>
/// Сравнение наборов SHA-256 signing-сертификатов APK и установленного приложения. Чистая
/// Application-логика без Android-типов: порядок hash-строк не важен, регистр hex игнорируется;
/// пустой набор с любой стороны считается несовпадением (установка без проверки подписи запрещена,
/// FR-005 009). Используется Platform.Android-адаптером установки.
/// </summary>
public static class SigningCertificateSetComparer
{
    /// <summary>
    /// true, только если оба набора непусты и содержат одинаковый набор SHA-256 hash-строк
    /// (без учёта порядка и регистра hex). Пустой набор с любой стороны → false.
    /// </summary>
    /// <param name="first">SHA-256 signing-сертификатов одной стороны (например, APK-обновления).</param>
    /// <param name="second">SHA-256 signing-сертификатов другой стороны (например, установленного приложения).</param>
    /// <returns>true, если наборы подписей считаются совпадающими.</returns>
    /// <exception cref="ArgumentNullException">Любая коллекция равна <see langword="null"/>.</exception>
    public static bool AreEqual(IReadOnlyCollection<string> first, IReadOnlyCollection<string> second)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (first.Count == 0 || second.Count == 0)
        {
            return false;
        }

        var firstSet = new HashSet<string>(first, StringComparer.OrdinalIgnoreCase);
        var secondSet = new HashSet<string>(second, StringComparer.OrdinalIgnoreCase);
        return firstSet.SetEquals(secondSet);
    }
}
