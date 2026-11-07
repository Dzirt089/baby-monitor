using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using BabyMonitor.App.Composition;
using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Monitoring;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.ForegroundServices;

namespace BabyMonitor.App;

/// <summary>
/// Экран мониторинга: start/stop foreground-сессии через Application-модель, состояние и typed
/// ошибки. Activity только связывает View и команды; строки — в resources.
/// </summary>
[Activity(Label = "@string/monitoring_label", Exported = false)]
public sealed class MonitoringActivity : PermissionActivity
{
    /// <summary>Intent extra с ролью (Child/Parent).</summary>
    public const string ExtraRole = "babymonitor.role";

    private readonly EventHandler _modelChangedHandler;
    private readonly CancellationTokenSource _lifetime = new();
    private AndroidForegroundSessionHost? _host;
    private MonitoringScreenModel? _model;
    private Task? _initialization;
    private TextView? _status;
    private TextView? _diagnostics;
    private TextView? _error;
    private ProgressBar? _progress;
    private Button? _start;
    private Button? _stop;
    private DeviceRole _role;

    /// <summary>Создаёт Activity.</summary>
    public MonitoringActivity()
    {
        _modelChangedHandler = (_, _) => RunOnUiThread(Render);
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        var roleText = Intent?.GetStringExtra(ExtraRole);
        _role = roleText switch
        {
            nameof(DeviceRole.Child) => DeviceRole.Child,
            nameof(DeviceRole.Parent) => DeviceRole.Parent,
            _ => DeviceRole.Unknown
        };
        if (_role is DeviceRole.Unknown)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_monitoring);
        Title = GetString(_role is DeviceRole.Child
            ? Resource.String.monitoring_title_child
            : Resource.String.monitoring_title_parent);

        _status = FindViewById<TextView>(Resource.Id.monitoring_status);
        _diagnostics = FindViewById<TextView>(Resource.Id.monitoring_diagnostics_text);
        _error = FindViewById<TextView>(Resource.Id.monitoring_error);
        _progress = FindViewById<ProgressBar>(Resource.Id.monitoring_progress);
        _start = FindViewById<Button>(Resource.Id.monitoring_start);
        _stop = FindViewById<Button>(Resource.Id.monitoring_stop);

        var composition = MonitoringComposition.Create(this, _role);
        _host = composition.Host;
        _model = composition.Model;
        _model.Changed += _modelChangedHandler;

        _start!.Click += async (_, _) => await ExecuteAsync(model => model.StartAsync(_lifetime.Token));
        _stop!.Click += async (_, _) => await ExecuteAsync(model => model.StopAsync(_lifetime.Token));
        FindViewById<Button>(Resource.Id.monitoring_diagnostics)!.Click += (_, _) => OpenDiagnostics();
        FindViewById<Button>(Resource.Id.monitoring_back)!.Click += (_, _) => Finish();

        Render();
        _initialization = InitializeAsync();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _lifetime.Cancel();
        if (_model is not null)
        {
            _model.Changed -= _modelChangedHandler;
            _model.Dispose();
        }

        _host?.Dispose();
        _host = null;
        _lifetime.Dispose();
        base.OnDestroy();
    }

    private async Task InitializeAsync()
    {
        try
        {
            if (_model is not null)
            {
                await _model.InitializeAsync(_lifetime.Token);
            }
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            RunOnUiThread(Render);
        }
        finally
        {
            RunOnUiThread(Render);
        }
    }

    private async Task ExecuteAsync(Func<MonitoringScreenModel, Task> action)
    {
        if (_model is null || !_model.IsInitialized || _initialization is { IsCompleted: false })
        {
            return;
        }

        try
        {
            await action(_model);
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            RunOnUiThread(Render);
        }
        finally
        {
            RunOnUiThread(Render);
        }
    }

    private void OpenDiagnostics()
    {
        var intent = new Intent(this, typeof(DiagnosticsActivity));
        intent.PutExtra(DiagnosticsActivity.ExtraRole, _role.ToString());
        StartActivity(intent);
    }

    private void Render()
    {
        if (_model is null)
        {
            return;
        }

        var state = _model.Snapshot?.State;
        if (_status is not null)
        {
            _status.Text = GetString(state switch
            {
                MonitoringForegroundState.Starting => Resource.String.monitoring_state_starting,
                MonitoringForegroundState.Running => Resource.String.monitoring_state_running,
                MonitoringForegroundState.Stopping => Resource.String.monitoring_state_stopping,
                MonitoringForegroundState.Stopped => Resource.String.monitoring_state_stopped,
                MonitoringForegroundState.Failed => Resource.String.monitoring_state_failed,
                _ => Resource.String.monitoring_state_initial
            });
        }

        if (_diagnostics is not null)
        {
            var roleText = GetString(_model.Role is DeviceRole.Child
                ? Resource.String.monitoring_role_child
                : Resource.String.monitoring_role_parent);
            var stateText = state?.ToString() ?? GetString(Resource.String.monitoring_state_initial);
            _diagnostics.Text = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                GetString(Resource.String.monitoring_diagnostics_format),
                roleText,
                stateText);
        }

        if (_error is not null)
        {
            var hasError = !string.IsNullOrWhiteSpace(_model.ErrorCode);
            _error.Visibility = hasError ? ViewStates.Visible : ViewStates.Gone;
            if (hasError)
            {
                _error.Text = string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    GetString(Resource.String.monitoring_error_format),
                    _model.ErrorCode,
                    _model.ErrorMessage);
            }
        }

        if (_progress is not null)
        {
            _progress.Visibility = _model.IsBusy ? ViewStates.Visible : ViewStates.Gone;
        }

        var snapshot = _model.Snapshot;
        if (_start is not null)
        {
            _start.Enabled = _model.IsInitialized && !_model.IsBusy && !_model.IsRoleMismatch
                && (snapshot is null || snapshot.IsTerminal);
        }

        if (_stop is not null)
        {
            _stop.Enabled = _model.IsInitialized && !_model.IsBusy && snapshot is not null && !snapshot.IsTerminal;
        }
    }
}
