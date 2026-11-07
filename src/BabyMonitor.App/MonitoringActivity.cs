using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using BabyMonitor.App.Presentation;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.ForegroundServices;
using BabyMonitor.Platform.Android.Permissions;

namespace BabyMonitor.App;

/// <summary>
/// Экран мониторинга: start/stop foreground-сессии через IForegroundSessionHost, состояние и
/// диагностика. Activity только связывает View и ViewModel; бизнес-логики здесь нет.
/// </summary>
[Activity(Label = "@string/monitoring_label", Exported = false)]
public sealed class MonitoringActivity : PermissionActivity
{
    /// <summary>Intent extra с ролью (Child/Parent).</summary>
    public const string ExtraRole = "babymonitor.role";

    private readonly EventHandler _viewModelChangedHandler;
    private AndroidForegroundSessionHost? _host;
    private MonitoringViewModel? _viewModel;
    private TextView? _status;
    private TextView? _diagnostics;
    private TextView? _error;
    private ProgressBar? _progress;
    private Button? _start;
    private Button? _stop;

    /// <summary>Создаёт Activity.</summary>
    public MonitoringActivity()
    {
        _viewModelChangedHandler = (_, _) => RunOnUiThread(Render);
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var roleText = Intent?.GetStringExtra(ExtraRole);
        if (!Enum.TryParse<DeviceRole>(roleText, ignoreCase: false, out var role)
            || role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_monitoring);
        Title = GetString(role is DeviceRole.Child
            ? Resource.String.monitoring_title_child
            : Resource.String.monitoring_title_parent);

        _status = FindViewById<TextView>(Resource.Id.monitoring_status);
        _diagnostics = FindViewById<TextView>(Resource.Id.monitoring_diagnostics);
        _error = FindViewById<TextView>(Resource.Id.monitoring_error);
        _progress = FindViewById<ProgressBar>(Resource.Id.monitoring_progress);
        _start = FindViewById<Button>(Resource.Id.monitoring_start);
        _stop = FindViewById<Button>(Resource.Id.monitoring_stop);

        _host = new AndroidForegroundSessionHost(
            this,
            new AndroidNotificationPermission(this, this),
            new AndroidMicrophonePermission(this, this),
            this);
        _viewModel = new MonitoringViewModel(_host, role);
        _viewModel.Changed += _viewModelChangedHandler;

        _start!.Click += async (_, _) => await ExecuteAsync(vm => vm.StartAsync(CancellationToken.None));
        _stop!.Click += async (_, _) => await ExecuteAsync(vm => vm.StopAsync(CancellationToken.None));
        FindViewById<Button>(Resource.Id.monitoring_back)!.Click += (_, _) => Finish();

        Render();
        _ = InitializeAsync();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        if (_viewModel is not null)
        {
            _viewModel.Changed -= _viewModelChangedHandler;
        }

        _host?.Dispose();
        _host = null;
        base.OnDestroy();
    }

    private async Task InitializeAsync()
    {
        try
        {
            await _viewModel!.InitializeAsync(CancellationToken.None);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            RunOnUiThread(() => ShowError(exception.GetType().Name));
        }
    }

    private async Task ExecuteAsync(Func<MonitoringViewModel, Task> action)
    {
        try
        {
            await action(_viewModel!);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            RunOnUiThread(() => ShowError(exception.GetType().Name));
        }
        finally
        {
            RunOnUiThread(Render);
        }
    }

    private void Render()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_status is not null)
        {
            _status.Text = _viewModel.StatusText;
        }

        if (_diagnostics is not null)
        {
            _diagnostics.Text = _viewModel.DiagnosticsText;
        }

        if (_error is not null)
        {
            var hasError = !string.IsNullOrWhiteSpace(_viewModel.ErrorText);
            _error.Visibility = hasError ? ViewStates.Visible : ViewStates.Gone;
            _error.Text = _viewModel.ErrorText;
        }

        if (_progress is not null)
        {
            _progress.Visibility = _viewModel.IsBusy ? ViewStates.Visible : ViewStates.Gone;
        }

        var snapshot = _viewModel.Snapshot;
        if (_start is not null)
        {
            _start.Enabled = !_viewModel.IsBusy && (snapshot is null || snapshot.IsTerminal);
        }

        if (_stop is not null)
        {
            _stop.Enabled = !_viewModel.IsBusy && snapshot is not null && !snapshot.IsTerminal;
        }
    }

    private void ShowError(string detail)
    {
        if (_error is null)
        {
            return;
        }

        _error.Visibility = ViewStates.Visible;
        _error.Text = GetString(Resource.String.monitoring_unexpected_error);
    }
}
