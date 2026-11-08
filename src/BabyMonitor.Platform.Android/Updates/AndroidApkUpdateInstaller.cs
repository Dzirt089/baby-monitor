using System.Runtime.Versioning;
using System.Security.Cryptography;
using Android.Content;
using Android.Content.PM;
using Android.Util;
using BabyMonitor.Application.Results;
using BabyMonitor.Application.Updates;
using AndroidUri = global::Android.Net.Uri;
using Result = BabyMonitor.Application.Results.Result;
using Signature = Android.Content.PM.Signature;

namespace BabyMonitor.Platform.Android.Updates;

/// <summary>
/// Установщик APK-обновления (009, ADR-0012): проверяет packageId/versionCode и SHA-256 signing
/// certificate против установленного приложения, затем запускает PackageInstaller-сессию с
/// обязательным подтверждением пользователя (silent install запрещён). Файл APK после постановки
/// сессии в коммит удаляется из кэша; секреты/PII не логируются.
/// </summary>
public sealed class AndroidApkUpdateInstaller : IUpdateInstaller
{
    private const string LogTag = "BabyMonitor.Updates";
    private const string ApkSessionName = "base.apk";
    private const int InstallRequestCode = 9101;

    /// <summary>PackageInstaller.SessionParams.USER_ACTION_REQUIRED (API 31+): подтверждение обязательно.</summary>
    private const int UserActionRequired = 1;

    private readonly Context _context;
    private readonly PackageManager? _packageManager;
    private readonly string _packageId;

    /// <summary>Создаёт установщик обновлений.</summary>
    /// <param name="context">Android context приложения (Activity не удерживается).</param>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">У context отсутствует PackageName.</exception>
    public AndroidApkUpdateInstaller(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context.ApplicationContext ?? context;
        _packageManager = _context.PackageManager;
        _packageId = _context.PackageName
            ?? throw new InvalidOperationException("PackageName приложения недоступен.");
    }

    /// <inheritdoc />
    public async Task<Result> InstallAsync(DownloadedUpdatePackage package, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(package);
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(package.FilePath))
        {
            return Failure(AppUpdateErrorCodes.InstallFailed, "Файл обновления не найден.");
        }

        try
        {
            var archive = _packageManager?.GetPackageArchiveInfo(package.FilePath, GetSignatureFlags());
            if (archive is null)
            {
                return Failure(AppUpdateErrorCodes.InstallFailed, "APK обновления не читается.");
            }

            if (!string.Equals(archive.PackageName, _packageId, StringComparison.Ordinal))
            {
                return Failure(AppUpdateErrorCodes.PackageMismatch, "Обновление относится к другому приложению.");
            }

            // Cross-check: содержимое APK должно соответствовать манифесту, по которому он скачан.
            var archiveVersionCode = PackageInfoVersionCode.Read(archive);
            if (archiveVersionCode != package.Manifest.VersionCode
                || (!string.IsNullOrEmpty(archive.VersionName)
                    && !string.Equals(archive.VersionName, package.Manifest.VersionName, StringComparison.Ordinal)))
            {
                return Failure(
                    AppUpdateErrorCodes.InstallFailed,
                    "APK не соответствует манифесту обновления.");
            }

            var installed = _packageManager?.GetPackageInfo(_packageId, GetSignatureFlags());
            if (installed is null)
            {
                return Failure(AppUpdateErrorCodes.InstallFailed, "Установленное приложение не найдено.");
            }

            if (archiveVersionCode <= PackageInfoVersionCode.Read(installed))
            {
                return Failure(
                    AppUpdateErrorCodes.DowngradeOrEqual,
                    "Версия обновления не новее установленной.");
            }

            var apkSignatures = GetSha256Signatures(archive);
            var installedSignatures = GetSha256Signatures(installed);
            if (!SigningCertificateSetComparer.AreEqual(apkSignatures, installedSignatures))
            {
                return Failure(
                    AppUpdateErrorCodes.SignatureMismatch,
                    "Подпись APK не совпадает с установленным приложением.");
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (OperatingSystem.IsAndroidVersionAtLeast(26) && _packageManager?.CanRequestPackageInstalls() is false)
            {
                OpenUnknownSourcesSettings();
                return Failure(
                    AppUpdateErrorCodes.UnknownSourcesRequired,
                    "Разрешите установку из неизвестных источников в настройках и повторите.");
            }

            return await StartInstallerSessionAsync(package.FilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (PackageManager.NameNotFoundException)
        {
            return Failure(AppUpdateErrorCodes.InstallFailed, "Установленное приложение не найдено.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "APK update check failed: " + exception.GetType().Name);
            return Failure(AppUpdateErrorCodes.InstallFailed, "Не удалось проверить обновление.");
        }
    }

    private async Task<Result> StartInstallerSessionAsync(string filePath, CancellationToken cancellationToken)
    {
        var installer = _packageManager?.PackageInstaller;
        if (installer is null)
        {
            return Failure(AppUpdateErrorCodes.InstallerUnavailable, "PackageInstaller недоступен на устройстве.");
        }

        var sessionId = -1;
        try
        {
            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
            {
                // Явно требуем подтверждение пользователя (silent install запрещён, ADR-0012).
                RequireUserAction(parameters);
            }

            sessionId = installer.CreateSession(parameters);
            var session = installer.OpenSession(sessionId);
            if (session is null)
            {
                AbandonSession(installer, sessionId);
                return Failure(AppUpdateErrorCodes.InstallerUnavailable, "Не удалось открыть сессию установки.");
            }

            try
            {
                await WriteSessionAsync(session, filePath, cancellationToken).ConfigureAwait(false);

                var statusIntent = new Intent(_context, typeof(UpdateInstallResultReceiver))
                    .SetPackage(_packageId);
                var statusReceiver = PendingIntent.GetBroadcast(
                    _context,
                    InstallRequestCode,
                    statusIntent,
                    GetStatusPendingIntentFlags());
                if (statusReceiver?.IntentSender is null)
                {
                    AbandonSession(installer, sessionId);
                    return Failure(AppUpdateErrorCodes.InstallerUnavailable, "Не удалось создать статус установки.");
                }

                session.Commit(statusReceiver.IntentSender);
            }
            finally
            {
                session.Dispose();
            }

            // Данные уже скопированы в сессию: исходный APK из кэша больше не нужен.
            TryDeleteFile(filePath);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            AbandonSession(installer, sessionId);
            throw;
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "APK installer session failed: " + exception.GetType().Name);
            AbandonSession(installer, sessionId);
            return Failure(AppUpdateErrorCodes.InstallFailed, "Не удалось запустить установку обновления.");
        }
    }

    private static async Task WriteSessionAsync(
        PackageInstaller.Session session,
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            useAsync: true);
        var output = session.OpenWrite(ApkSessionName, 0, input.Length);
        if (output is null)
        {
            throw new IOException("PackageInstaller session output stream unavailable.");
        }

        try
        {
            await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            session.Fsync(output);
        }
        finally
        {
            output.Dispose();
        }
    }

    [SupportedOSPlatform("android31.0")]
    private static void RequireUserAction(PackageInstaller.SessionParams parameters) =>
        parameters.SetRequireUserAction(UserActionRequired);

    [SupportedOSPlatform("android26.0")]
    private void OpenUnknownSourcesSettings()
    {
        try
        {
            var intent = new Intent(global::Android.Provider.Settings.ActionManageUnknownAppSources)
                .SetData(AndroidUri.Parse("package:" + _packageId))
                .AddFlags(ActivityFlags.NewTask);
            _context.StartActivity(intent);
        }
        catch (ActivityNotFoundException exception)
        {
            Log.Warn(LogTag, "Unknown sources settings unavailable: " + exception.GetType().Name);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Unknown sources settings failed: " + exception.GetType().Name);
        }
    }

    private static PackageInfoFlags GetSignatureFlags()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            return PackageInfoFlags.SigningCertificates;
        }

#pragma warning disable CA1422 // Signatures устарел на API 28+; legacy-флаг для API 24–27.
        return PackageInfoFlags.Signatures;
#pragma warning restore CA1422
    }

    private static IReadOnlyCollection<string> GetSha256Signatures(PackageInfo packageInfo)
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (OperatingSystem.IsAndroidVersionAtLeast(28))
        {
            var signers = packageInfo.SigningInfo?.GetApkContentsSigners();
            if (signers is not null)
            {
                foreach (var signer in signers)
                {
                    AddSignatureHash(hashes, signer);
                }
            }
        }
        else
        {
#pragma warning disable CA1422 // Signatures устарел на API 28+; legacy-ветка для API 24–27.
            var signatures = packageInfo.Signatures;
#pragma warning restore CA1422
            if (signatures is not null)
            {
                foreach (var signature in signatures)
                {
                    AddSignatureHash(hashes, signature);
                }
            }
        }

        return hashes;
    }

    private static void AddSignatureHash(HashSet<string> hashes, Signature? signature)
    {
        if (signature is null)
        {
            return;
        }

        var certificateBytes = signature.ToByteArray();
        if (certificateBytes is null || certificateBytes.Length == 0)
        {
            return;
        }

        hashes.Add(Convert.ToHexStringLower(SHA256.HashData(certificateBytes)));
    }

    private static PendingIntentFlags GetStatusPendingIntentFlags()
    {
        var flags = PendingIntentFlags.UpdateCurrent;
        if (OperatingSystem.IsAndroidVersionAtLeast(31))
        {
            // PackageInstaller на Android 12+ требует mutable PendingIntent для результата установки.
            flags |= PendingIntentFlags.Mutable;
        }

        return flags;
    }

    private static void AbandonSession(PackageInstaller installer, int sessionId)
    {
        if (sessionId < 0)
        {
            return;
        }

        try
        {
            installer.AbandonSession(sessionId);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Installer session abandon failed: " + exception.GetType().Name);
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            File.Delete(filePath);
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Update APK cache cleanup failed: " + exception.GetType().Name);
        }
    }

    private static Result Failure(string code, string message) => Result.Failure(new Error(code, message));
}
