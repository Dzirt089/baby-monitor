namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Сравнение наборов SHA-256 signing certificate APK и установленного приложения. Чистая логика
/// без Android API: пригодна для unit-тестов (проверяет смену подписи, порядок и регистр hex).
/// </summary>
internal static class SignatureSetComparer
{
    /// <summary>
    /// true, если оба набора непусты и содержат одинаковый набор SHA-256 (без учёта порядка,
    /// с игнорированием регистра hex). Пустой набор считается несовпадением: установка без
    /// проверки подписи запрещена.
    /// </summary>
    /// <param name="apkSignatures">SHA-256 сертификатов APK-обновления.</param>
    /// <param name="installedSignatures">SHA-256 сертификатов установленного приложения.</param>
    /// <returns>true, если подписи считаются совпадающими.</returns>
    public static bool AreEqual(
        IReadOnlyCollection<string> apkSignatures,
        IReadOnlyCollection<string> installedSignatures)
    {
        ArgumentNullException.ThrowIfNull(apkSignatures);
        ArgumentNullException.ThrowIfNull(installedSignatures);

        if (apkSignatures.Count == 0 || installedSignatures.Count == 0)
        {
            return false;
        }

        var apkSet = new HashSet<string>(apkSignatures, StringComparer.OrdinalIgnoreCase);
        var installedSet = new HashSet<string>(installedSignatures, StringComparer.OrdinalIgnoreCase);
        return apkSet.SetEquals(installedSet);
    }
}
