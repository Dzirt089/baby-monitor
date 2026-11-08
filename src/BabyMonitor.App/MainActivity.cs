using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Util;
using Android.Widget;
using BabyMonitor.App.Composition;
using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Updates;
using BabyMonitor.Domain.Devices;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.App;

/// <summary>
/// Экран выбора роли. Роль выбирается явно и передаётся в PairingActivity (006 phase 2);
/// скрытого default нет, сопряжение предшествует мониторингу. Здесь же — кнопка «Обновления» (009)
/// и фоновая автопроверка обновлений при запуске: не блокирует UI, Wi-Fi-only и суточный throttle
/// проверяет Application-координатор, пользователю при найденном обновлении показывается диалог.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : PermissionActivity
{
    private const string LogTag = "BabyMonitor.Updates";

    private CancellationTokenSource? _autoCheckCts;
    private Task? _autoCheckTask;

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        FindViewById<Button>(Resource.Id.role_child)!.Click += (_, _) => OpenPairing(DeviceRole.Child);
        FindViewById<Button>(Resource.Id.role_parent)!.Click += (_, _) => OpenPairing(DeviceRole.Parent);
        FindViewById<Button>(Resource.Id.updates_button)!.Click += (_, _) => OpenUpdates();

        StartAutoUpdateCheck();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        if (_autoCheckCts is not null)
        {
            _autoCheckCts.Cancel();
            _autoCheckCts.Dispose();
            _autoCheckCts = null;
        }

        base.OnDestroy();
    }

    private void OpenPairing(DeviceRole role)
    {
        var intent = new Intent(this, typeof(PairingActivity));
        intent.PutExtra(PairingActivity.ExtraRole, role.ToString());
        StartActivity(intent);
    }

    private void OpenUpdates(bool checkImmediately = false)
    {
        var intent = new Intent(this, typeof(UpdateActivity));
        if (checkImmediately)
        {
            // Диалог «Доступно обновление»: экран сразу показывает найденное обновление.
            intent.PutExtra(UpdateActivity.ExtraAutoCheckOnOpen, true);
        }

        StartActivity(intent);
    }

    /// <summary>
    /// Запускает фоновую автопроверку обновлений, если она включена. Wi-Fi gate и суточный throttle
    /// выполняет AppUpdateCoordinator; проверка не блокирует UI и не мешает мониторингу.
    /// </summary>
    private void StartAutoUpdateCheck()
    {
        var updates = (Application as BabyMonitorApplication)?.Updates;
        if (updates is null || !updates.AutoCheckEnabled)
        {
            return;
        }

        _autoCheckCts = new CancellationTokenSource();
        _autoCheckTask = RunAutoUpdateCheckAsync(updates, _autoCheckCts.Token);
    }

    private async Task RunAutoUpdateCheckAsync(UpdateComposition updates, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(
                () => updates.CheckAutomaticAsync(cancellationToken),
                cancellationToken);
            if (cancellationToken.IsCancellationRequested || !result.IsSuccess || result.Value is null)
            {
                return;
            }

            RunOnUiThread(() => ShowUpdateAvailableDialog(result.Value));
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана — штатная отмена фоновой проверки, не ошибка.
        }
        catch (Exception exception)
        {
            Log.Warn(LogTag, "Auto update check failed: " + exception.GetType().Name);
        }
    }

    private void ShowUpdateAvailableDialog(AppUpdateInfo update)
    {
        if (IsFinishing || IsDestroyed)
        {
            return;
        }

        var builder = new AlertDialog.Builder(this)!;
        builder.SetTitle(GetString(Resource.String.update_available_title));
        builder.SetMessage(string.Format(
            CultureInfo.InvariantCulture,
            GetString(Resource.String.update_available_message),
            update.Manifest.VersionName));
        builder.SetPositiveButton(
            GetString(Resource.String.update_available_now),
            (_, _) => OpenUpdates(checkImmediately: true));
        builder.SetNegativeButton(GetString(Resource.String.update_available_later), (_, _) => { });
        builder.Show();
    }
}
