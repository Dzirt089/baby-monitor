using System.Globalization;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Widget;
using BabyMonitor.App.Composition;
using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Diagnostics;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.ForegroundServices;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.App;

/// <summary>
/// Экран диагностики (006 phase 2): роль, состояние foreground-сессии, последняя безопасная ошибка,
/// доверенные устройства (роль/fingerprint short/время) и версия приложения. Не показывает аудио,
/// секреты и персональные данные; media честно помечена как недоступная (005 phase 2 BLOCKED).
/// </summary>
[Activity(Label = "@string/diagnostics_label", Exported = false)]
public sealed class DiagnosticsActivity : PermissionActivity
{
    /// <summary>Intent extra с ролью (Child/Parent).</summary>
    public const string ExtraRole = "babymonitor.role";

    private readonly EventHandler _modelChangedHandler;
    private readonly CancellationTokenSource _lifetime = new();
    private AndroidForegroundSessionHost? _host;
    private DiagnosticsScreenModel? _model;
    private DeviceRole _role;
    private bool _destroyed;

    private TextView? _roleText;
    private TextView? _foregroundText;
    private TextView? _lastErrorText;
    private TextView? _appVersionText;
    private TextView? _errorText;
    private ProgressBar? _progress;
    private LinearLayout? _trustedList;
    private TextView? _trustedEmpty;
    private Button? _refresh;
    private Button? _back;

    /// <summary>Создаёт Activity.</summary>
    public DiagnosticsActivity()
    {
        _modelChangedHandler = (_, _) => RunOnUiThread(Render);
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _role = Intent?.GetStringExtra(ExtraRole) switch
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

        SetContentView(Resource.Layout.activity_diagnostics);
        Title = GetString(Resource.String.diagnostics_title);

        _roleText = FindViewById<TextView>(Resource.Id.diagnostics_role);
        _foregroundText = FindViewById<TextView>(Resource.Id.diagnostics_foreground);
        _lastErrorText = FindViewById<TextView>(Resource.Id.diagnostics_last_error);
        _appVersionText = FindViewById<TextView>(Resource.Id.diagnostics_app_version);
        _errorText = FindViewById<TextView>(Resource.Id.diagnostics_error);
        _progress = FindViewById<ProgressBar>(Resource.Id.diagnostics_progress);
        _trustedList = FindViewById<LinearLayout>(Resource.Id.diagnostics_trusted_list);
        _trustedEmpty = FindViewById<TextView>(Resource.Id.diagnostics_trusted_empty);
        _refresh = FindViewById<Button>(Resource.Id.diagnostics_refresh);
        _back = FindViewById<Button>(Resource.Id.diagnostics_back);

        var composition = DiagnosticsComposition.Create(this, _role);
        _host = composition.Host;
        _model = composition.Model;
        _model.Changed += _modelChangedHandler;

        _refresh!.Click += async (_, _) => await RefreshAsync();
        _back!.Click += (_, _) => Finish();

        _appVersionText!.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetString(Resource.String.diagnostics_app_version_format),
            GetAppVersion());

        Render();
        InitializeAsync();
    }

    private async void InitializeAsync()
    {
        try
        {
            await RefreshScreenAsync();
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана во время первичного bind/refresh — штатная отмена, не ошибка.
        }
        catch (Exception exception)
        {
            // InitializeAsync — UI entry point: ошибка уже отображена через RefreshScreenAsync.
            _errorText!.Visibility = ViewStates.Visible;
            _errorText.Text = string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.diagnostics_error_format),
                "diagnostics.failed." + exception.GetType().Name,
                GetString(Resource.String.diagnostics_unexpected_error));
        }
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _destroyed = true;
        _lifetime.Cancel();
        if (_model is not null)
        {
            _model.Changed -= _modelChangedHandler;
            _model.Dispose();
            _model = null;
        }

        _host?.Dispose();
        _host = null;
        _lifetime.Dispose();
        base.OnDestroy();
    }

    private async Task RefreshScreenAsync()
    {
        if (_destroyed || _model is null)
        {
            return;
        }

        try
        {
            await _model.RefreshAsync(_lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана/отмена обслуживания — штатная отмена, не ошибка.
        }
        catch (Exception exception)
        {
            // Экран диагностики не должен падать: ошибка уже отображается через модель.
            _errorText!.Visibility = ViewStates.Visible;
            _errorText.Text = string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.diagnostics_error_format),
                "diagnostics.failed." + exception.GetType().Name,
                GetString(Resource.String.diagnostics_unexpected_error));
        }
        finally
        {
            Render();
        }
    }

    private async Task RefreshAsync()
    {
        if (_destroyed)
        {
            return;
        }

        await RefreshScreenAsync();
    }

    private void Render()
    {
        if (_destroyed || _model is null)
        {
            return;
        }

        var snapshot = _model.Snapshot;
        _roleText!.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetString(Resource.String.diagnostics_role_format),
            GetString(_role is DeviceRole.Child
                ? Resource.String.monitoring_role_child
                : Resource.String.monitoring_role_parent));

        var foreground = snapshot?.Foreground;
        _foregroundText!.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetString(Resource.String.diagnostics_foreground_format),
            foreground is null
                ? GetString(Resource.String.diagnostics_foreground_none)
                : GetString(MapForegroundState(foreground.State)));

        _lastErrorText!.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetString(Resource.String.diagnostics_last_error_format),
            foreground?.Error?.Code ?? GetString(Resource.String.diagnostics_last_error_none));

        _progress!.Visibility = _model.IsBusy ? ViewStates.Visible : ViewStates.Gone;
        _refresh!.Enabled = !_model.IsBusy;

        _errorText!.Visibility = string.IsNullOrWhiteSpace(_model.ErrorCode) ? ViewStates.Gone : ViewStates.Visible;
        if (!string.IsNullOrWhiteSpace(_model.ErrorCode))
        {
            _errorText.Text = string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.diagnostics_error_format),
                _model.ErrorCode,
                _model.ErrorMessage);
        }

        RenderTrustedDevices(snapshot?.TrustedDevices ?? Array.Empty<PairedDeviceSummary>());
    }

    private void RenderTrustedDevices(IReadOnlyList<PairedDeviceSummary> devices)
    {
        if (_trustedList is null || _trustedEmpty is null)
        {
            return;
        }

        _trustedList.RemoveAllViews();
        _trustedEmpty.Visibility = devices.Count == 0 ? ViewStates.Visible : ViewStates.Gone;

        foreach (var device in devices)
        {
            var item = new TextView(this)
            {
                Text = string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(Resource.String.diagnostics_trusted_item_format),
                    GetString(device.PeerRole is DeviceRole.Child
                        ? Resource.String.monitoring_role_child
                        : Resource.String.monitoring_role_parent),
                    device.FingerprintShort,
                    device.PairedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)),
                TextSize = 14
            };
            _trustedList.AddView(item);
        }
    }

    private string GetAppVersion()
    {
        try
        {
            var packageInfo = PackageManager?.GetPackageInfo(PackageName!, 0);
            return packageInfo?.VersionName ?? "?";
        }
        catch (PackageManager.NameNotFoundException)
        {
            return "?";
        }
    }

    private static int MapForegroundState(MonitoringForegroundState state) => state switch
    {
        MonitoringForegroundState.Starting => Resource.String.monitoring_state_starting,
        MonitoringForegroundState.Running => Resource.String.monitoring_state_running,
        MonitoringForegroundState.Stopping => Resource.String.monitoring_state_stopping,
        MonitoringForegroundState.Stopped => Resource.String.monitoring_state_stopped,
        MonitoringForegroundState.Failed => Resource.String.monitoring_state_failed,
        _ => Resource.String.monitoring_state_initial
    };
}
