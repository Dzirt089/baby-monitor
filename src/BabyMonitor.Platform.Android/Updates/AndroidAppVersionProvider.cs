using Android.Content;
using Android.Content.PM;
using BabyMonitor.Application.Updates;
using BabyMonitor.Domain.Updates;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Провайдер установленной версии приложения: packageId из context, versionName/versionCode из
/// PackageManager. Не содержит секретов/PII и не удерживает Activity (только application context).
/// </summary>
public sealed class AndroidAppVersionProvider : IAppVersionProvider
{
    private const string UnknownVersionName = "?";

    private readonly PackageManager? _packageManager;
    private readonly string _packageId;

    /// <summary>Создаёт провайдер версии приложения.</summary>
    /// <param name="context">Android context приложения.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">У context отсутствует PackageName.</exception>
    public AndroidAppVersionProvider(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var applicationContext = context.ApplicationContext ?? context;
        _packageManager = applicationContext.PackageManager;
        _packageId = applicationContext.PackageName
            ?? throw new InvalidOperationException("PackageName приложения недоступен.");
    }

    /// <inheritdoc />
    public string PackageId => _packageId;

    /// <inheritdoc />
    public AppVersion GetCurrentVersion()
    {
        try
        {
            var packageInfo = _packageManager?.GetPackageInfo(_packageId, 0);
            if (packageInfo is null)
            {
                return new AppVersion(0, UnknownVersionName);
            }

            return new AppVersion(
                PackageInfoVersionCode.Read(packageInfo),
                packageInfo.VersionName ?? UnknownVersionName);
        }
        catch (PackageManager.NameNotFoundException)
        {
            // Пакет не найден — неожиданно для собственного приложения, но не повод падать.
            return new AppVersion(0, UnknownVersionName);
        }
    }
}
