using Android.Content.PM;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Чтение versionCode из <see cref="PackageInfo"/> с учётом API: LongVersionCode на API 28+,
/// legacy VersionCode на API 24–27. Вынесено отдельно как чистый helper для проверки в тестах.
/// </summary>
internal static class PackageInfoVersionCode
{
    /// <summary>Возвращает versionCode пакета (в диапазоне int: Android допускает до 2100000000).</summary>
    /// <param name="packageInfo">Метаданные установленного/архивного пакета.</param>
    /// <returns>Целочисленный versionCode.</returns>
    public static int Read(PackageInfo packageInfo)
    {
        ArgumentNullException.ThrowIfNull(packageInfo);

        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return (int)packageInfo.LongVersionCode;
        }

#pragma warning disable CA1422 // VersionCode устарел на API 28+; legacy-ветка нужна для API 24–27.
        return packageInfo.VersionCode;
#pragma warning restore CA1422
    }
}
