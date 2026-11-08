namespace BabyMonitor.Domain.Updates;

/// <summary>
/// Версия приложения: целочисленный <paramref name="VersionCode"/> (источник истины для сравнения)
/// и человекочитаемый <paramref name="VersionName"/> для отображения. Immutable value object;
/// не содержит платформенных типов (Android versionCode/versionName маппятся адаптером).
/// </summary>
/// <param name="VersionCode">Целочисленный код версии; большее значение — более новая версия.</param>
/// <param name="VersionName">Человекочитаемое имя версии (например, 0.5.1).</param>
public readonly record struct AppVersion(int VersionCode, string VersionName)
{
    /// <summary>
    /// Сравнивает версии только по <see cref="VersionCode"/>: положительное значение, если
    /// <paramref name="left"/> новее <paramref name="right"/>; ноль — равны; отрицательное — старее.
    /// <see cref="VersionName"/> на сравнение не влияет.
    /// </summary>
    /// <param name="left">Левая версия.</param>
    /// <param name="right">Правая версия.</param>
    /// <returns>Знак разности versionCode.</returns>
    public static int Compare(AppVersion left, AppVersion right) =>
        left.VersionCode.CompareTo(right.VersionCode);

    /// <summary>
    /// true, только если <paramref name="candidate"/> строго новее <paramref name="current"/>
    /// (versionCode больше). Equal и downgrade возвращают false: такие обновления запрещены.
    /// </summary>
    /// <param name="candidate">Предлагаемая версия обновления.</param>
    /// <param name="current">Установленная версия.</param>
    /// <returns>true, если обновление допустимо по версии.</returns>
    public static bool IsNewerThan(AppVersion candidate, AppVersion current) =>
        candidate.VersionCode > current.VersionCode;
}
