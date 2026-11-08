using System.Globalization;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using BabyMonitor.App.Composition;
using BabyMonitor.App.Presentation;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.App;

/// <summary>
/// Экран «Обновления» (009, ADR-0012): текущая/доступная версия, ручная проверка, скачивание и
/// установка через PackageInstaller (подтверждение пользователя обязательно), чекбокс авто-проверки.
/// Activity только связывает View и ViewModel; строки — в resources.
/// </summary>
[Activity(Label = "@string/update_label", Exported = false)]
public sealed class UpdateActivity : PermissionActivity
{
    private readonly EventHandler _modelChangedHandler;
    private readonly CancellationTokenSource _lifetime = new();
    private UpdateViewModel? _model;
    private TextView? _currentVersion;
    private TextView? _availableVersion;
    private TextView? _status;
    private TextView? _error;
    private ProgressBar? _progress;
    private CheckBox? _autoCheck;
    private Button? _check;
    private Button? _install;
    private Button? _back;
    private bool _suppressAutoCheck;

    /// <summary>Создаёт Activity.</summary>
    public UpdateActivity()
    {
        _modelChangedHandler = (_, _) => RunOnUiThread(Render);
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetContentView(Resource.Layout.activity_update);
        Title = GetString(Resource.String.update_title);

        _currentVersion = FindViewById<TextView>(Resource.Id.update_current_version);
        _availableVersion = FindViewById<TextView>(Resource.Id.update_available_version);
        _status = FindViewById<TextView>(Resource.Id.update_status);
        _error = FindViewById<TextView>(Resource.Id.update_error);
        _progress = FindViewById<ProgressBar>(Resource.Id.update_progress);
        _autoCheck = FindViewById<CheckBox>(Resource.Id.update_auto_check);
        _check = FindViewById<Button>(Resource.Id.update_check);
        _install = FindViewById<Button>(Resource.Id.update_install);
        _back = FindViewById<Button>(Resource.Id.update_back);

        var updates = (Application as BabyMonitorApplication)?.Updates;
        if (updates is null)
        {
            // Composition root не инициализирован — экран не может работать, но не падает.
            Finish();
            return;
        }

        _model = updates.CreateViewModel();
        _model.Changed += _modelChangedHandler;
        _model.Initialize();

        _check!.Click += async (_, _) => await ExecuteAsync(token => _model.CheckAsync(token));
        _install!.Click += async (_, _) => await ExecuteAsync(token => _model.DownloadAndInstallAsync(token));
        _back!.Click += (_, _) => Finish();
        _autoCheck!.CheckedChange += OnAutoCheckChanged;

        Render();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _lifetime.Cancel();
        if (_model is not null)
        {
            _model.Changed -= _modelChangedHandler;
            _model.Dispose();
            _model = null;
        }

        _lifetime.Dispose();
        base.OnDestroy();
    }

    private void OnAutoCheckChanged(object? sender, CompoundButton.CheckedChangeEventArgs args)
    {
        if (_suppressAutoCheck)
        {
            return;
        }

        _model?.SetAutoCheckEnabled(args.IsChecked);
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action)
    {
        if (_model is null)
        {
            return;
        }

        try
        {
            await action(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана во время проверки/загрузки — штатная отмена, не ошибка.
        }
        catch (Exception exception)
        {
            Android.Util.Log.Warn("BabyMonitor.Updates", "Update screen action failed: " + exception.GetType().Name);
        }
        finally
        {
            RunOnUiThread(Render);
        }
    }

    private void Render()
    {
        if (_model is null || IsFinishing || IsDestroyed)
        {
            return;
        }

        if (_currentVersion is not null)
        {
            _currentVersion.Text = string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.update_current_version_format),
                _model.CurrentVersionName);
        }

        var update = _model.AvailableUpdate;
        if (_availableVersion is not null)
        {
            _availableVersion.Visibility = update is null ? ViewStates.Gone : ViewStates.Visible;
            if (update is not null)
            {
                _availableVersion.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(Resource.String.update_latest_version_format),
                    update.Manifest.VersionName);
            }
        }

        if (_status is not null)
        {
            _status.Text = GetString(MapStatus(_model.Status));
        }

        if (_error is not null)
        {
            var hasError = !string.IsNullOrWhiteSpace(_model.ErrorCode);
            _error.Visibility = hasError ? ViewStates.Visible : ViewStates.Gone;
            if (hasError)
            {
                _error.Text = string.Format(
                    CultureInfo.InvariantCulture,
                    GetString(Resource.String.update_error_format),
                    _model.ErrorCode,
                    _model.ErrorMessage);
            }
        }

        if (_progress is not null)
        {
            var downloading = _model.Status is UpdateScreenStatus.Downloading;
            _progress.Visibility = downloading ? ViewStates.Visible : ViewStates.Gone;
            _progress.Progress = (int)Math.Round(
                _model.DownloadProgress * 100d,
                MidpointRounding.AwayFromZero);
        }

        if (_autoCheck is not null)
        {
            _suppressAutoCheck = true;
            _autoCheck.Checked = _model.AutoCheckEnabled;
            _suppressAutoCheck = false;
            _autoCheck.Enabled = !_model.IsBusy;
        }

        if (_check is not null)
        {
            _check.Enabled = !_model.IsBusy;
        }

        if (_install is not null)
        {
            _install.Enabled = !_model.IsBusy && update is not null;
        }
    }

    private static int MapStatus(UpdateScreenStatus status) => status switch
    {
        UpdateScreenStatus.Checking => Resource.String.update_status_checking,
        UpdateScreenStatus.UpToDate => Resource.String.update_status_up_to_date,
        UpdateScreenStatus.UpdateAvailable => Resource.String.update_status_update_available,
        UpdateScreenStatus.Downloading => Resource.String.update_status_downloading,
        UpdateScreenStatus.InstallRequested => Resource.String.update_status_install_requested,
        UpdateScreenStatus.Failed => Resource.String.update_status_failed,
        _ => Resource.String.update_status_idle
    };
}
