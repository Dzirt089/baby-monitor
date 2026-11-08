using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Application.Updates;

/// <summary>
/// Порт текущей версии приложения: versionCode/versionName и Android packageId.
/// Реализация — Platform.Android (PackageManager/ApplicationInfo).
/// </summary>
public interface IAppVersionProvider
{
    /// <summary>PackageId установленного приложения (для сверки с манифестом).</summary>
    string PackageId { get; }

    /// <summary>Текущая установленная версия приложения.</summary>
    /// <returns>Версия приложения.</returns>
    AppVersion GetCurrentVersion();
}
