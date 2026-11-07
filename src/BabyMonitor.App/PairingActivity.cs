using System.Globalization;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Widget;
using BabyMonitor.App.Composition;
using BabyMonitor.App.Presentation;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Platform.Android.Qr;
using OperationCanceledException = System.OperationCanceledException;

namespace BabyMonitor.App;

/// <summary>
/// Экран сопряжения (006 phase 2): Child показывает QR приглашения и сканирует QR-ответ Parent,
/// Parent сканирует QR приглашения и показывает QR-ответ. Оба устройства показывают одинаковый
/// verification code; доверие сохраняется только после подтверждения. Activity только связывает
/// View и команды screen model: камера/QR bitmap/строки — здесь, протокол — в Application.
/// </summary>
[Activity(Label = "@string/pairing_label", Exported = false)]
public sealed class PairingActivity : PermissionActivity
{
    /// <summary>Intent extra с ролью (Child/Parent).</summary>
    public const string ExtraRole = "babymonitor.role";

    private const long ExpiryTickMilliseconds = 1000;
    private const int QrSizePixels = 720;
    private const string QrRenderFailedErrorCode = "pairing.qr_render_failed";

    private readonly EventHandler _modelChangedHandler;
    private readonly CancellationTokenSource _lifetime = new();
    private PairingComposition? _composition;
    private DeviceRole _role;
    private ChildPairingScreenModel? _childModel;
    private ParentPairingScreenModel? _parentModel;
    private Timer? _expiryTimer;
    private bool _cameraActive;
    private bool _repairRequested;
    private bool _destroyed;
    private string? _localErrorCode;
    private string? _localErrorMessage;
    private string? _renderedQrPayload;
    private string? _renderedResponsePayload;
    private Android.Graphics.Bitmap? _qrBitmap;
    private Android.Graphics.Bitmap? _responseQrBitmap;

    private TextView? _status;
    private TextView? _note;
    private TextView? _error;
    private ProgressBar? _progress;
    private View? _cameraContainer;
    private TextureView? _cameraPreview;
    private Button? _cameraCancel;
    private View? _childSection;
    private ImageView? _qrImage;
    private TextView? _expiry;
    private View? _parentSection;
    private TextView? _responseNote;
    private ImageView? _responseQrImage;
    private View? _scanControls;
    private Button? _scan;
    private EditText? _paste;
    private Button? _pasteApply;
    private View? _verificationSection;
    private TextView? _verificationCode;
    private Button? _confirm;
    private Button? _reject;
    private View? _trustedSection;
    private TextView? _trustedInfo;
    private Button? _continueMonitoring;
    private Button? _forget;
    private Button? _repair;
    private Button? _skip;
    private Button? _back;

    /// <summary>Создаёт Activity.</summary>
    public PairingActivity()
    {
        _modelChangedHandler = (_, _) => RunOnUiThread(Render);
    }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        _role = ParseRole(Intent);
        if (_role is DeviceRole.Unknown)
        {
            Finish();
            return;
        }

        SetContentView(Resource.Layout.activity_pairing);
        Title = GetString(_role is DeviceRole.Child
            ? Resource.String.pairing_title_child
            : Resource.String.pairing_title_parent);

        BindViews();
        _composition = new PairingComposition(this, _role);
        _composition.Scanner.CodeDetected += OnScannerCodeDetected;
        _composition.Scanner.Failed += OnScannerFailed;
        _composition.TrustedModel.Changed += _modelChangedHandler;
        CreateFlowModel();

        BindActions();
        Render();
        InitializeAsync();
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();
        StartExpiryTimer();
        Render();
    }

    /// <inheritdoc />
    protected override void OnPause()
    {
        StopExpiryTimer();
        StopCamera(render: false);
        base.OnPause();
    }

    /// <inheritdoc />
    protected override void OnDestroy()
    {
        _destroyed = true;
        _lifetime.Cancel();
        StopExpiryTimer();

        if (_composition is not null)
        {
            _composition.Scanner.CodeDetected -= OnScannerCodeDetected;
            _composition.Scanner.Failed -= OnScannerFailed;
            _composition.TrustedModel.Changed -= _modelChangedHandler;
        }

        _childModel?.Changed -= _modelChangedHandler;
        _childModel?.Dispose();
        _childModel = null;
        _parentModel?.Changed -= _modelChangedHandler;
        _parentModel?.Dispose();
        _parentModel = null;

        _composition?.Dispose();
        _composition = null;

        _qrBitmap?.Recycle();
        _qrBitmap = null;
        _responseQrBitmap?.Recycle();
        _responseQrBitmap = null;

        _lifetime.Dispose();
        base.OnDestroy();
    }

    private static DeviceRole ParseRole(Intent? intent) =>
        intent?.GetStringExtra(ExtraRole) switch
        {
            nameof(DeviceRole.Child) => DeviceRole.Child,
            nameof(DeviceRole.Parent) => DeviceRole.Parent,
            _ => DeviceRole.Unknown
        };

    private void BindViews()
    {
        _status = FindViewById<TextView>(Resource.Id.pairing_status);
        _note = FindViewById<TextView>(Resource.Id.pairing_note);
        _error = FindViewById<TextView>(Resource.Id.pairing_error);
        _progress = FindViewById<ProgressBar>(Resource.Id.pairing_progress);
        _cameraContainer = FindViewById<View>(Resource.Id.pairing_camera_container);
        _cameraPreview = FindViewById<TextureView>(Resource.Id.pairing_camera_preview);
        _cameraCancel = FindViewById<Button>(Resource.Id.pairing_camera_cancel);
        _childSection = FindViewById<View>(Resource.Id.pairing_child_section);
        _qrImage = FindViewById<ImageView>(Resource.Id.pairing_qr);
        _expiry = FindViewById<TextView>(Resource.Id.pairing_expiry);
        _parentSection = FindViewById<View>(Resource.Id.pairing_parent_section);
        _responseNote = FindViewById<TextView>(Resource.Id.pairing_response_note);
        _responseQrImage = FindViewById<ImageView>(Resource.Id.pairing_response_qr);
        _scanControls = FindViewById<View>(Resource.Id.pairing_scan_controls);
        _scan = FindViewById<Button>(Resource.Id.pairing_scan);
        _paste = FindViewById<EditText>(Resource.Id.pairing_paste);
        _pasteApply = FindViewById<Button>(Resource.Id.pairing_paste_apply);
        _verificationSection = FindViewById<View>(Resource.Id.pairing_verification_section);
        _verificationCode = FindViewById<TextView>(Resource.Id.pairing_verification_code);
        _confirm = FindViewById<Button>(Resource.Id.pairing_confirm);
        _reject = FindViewById<Button>(Resource.Id.pairing_reject);
        _trustedSection = FindViewById<View>(Resource.Id.pairing_trusted_section);
        _trustedInfo = FindViewById<TextView>(Resource.Id.pairing_trusted_info);
        _continueMonitoring = FindViewById<Button>(Resource.Id.pairing_continue_monitoring);
        _forget = FindViewById<Button>(Resource.Id.pairing_forget);
        _repair = FindViewById<Button>(Resource.Id.pairing_repair);
        _skip = FindViewById<Button>(Resource.Id.pairing_skip);
        _back = FindViewById<Button>(Resource.Id.pairing_back);
    }

    private void BindActions()
    {
        _scan!.Click += async (_, _) => await ExecuteAsync(HandleScanButtonAsync);
        _cameraCancel!.Click += (_, _) => StopCamera(render: true);
        _pasteApply!.Click += async (_, _) => await ExecuteAsync(ApplyPastedPayloadAsync);
        _confirm!.Click += async (_, _) => await ExecuteAsync(() => ConfirmAsync(userConfirmed: true));
        _reject!.Click += async (_, _) => await ExecuteAsync(() => ConfirmAsync(userConfirmed: false));
        _continueMonitoring!.Click += (_, _) => OpenMonitoring();
        _skip!.Click += (_, _) => OpenMonitoring();
        _back!.Click += (_, _) => Finish();
        _forget!.Click += (_, _) => ShowForgetConfirmation();
        _repair!.Click += async (_, _) => await ExecuteAsync(StartRepairAsync);
    }

    private void CreateFlowModel()
    {
        if (_composition is null)
        {
            return;
        }

        if (_role is DeviceRole.Child)
        {
            _childModel = _composition.CreateChildModel();
            _childModel.Changed += _modelChangedHandler;
        }
        else
        {
            _parentModel = _composition.CreateParentModel();
            _parentModel.Changed += _modelChangedHandler;
        }
    }

    private async void InitializeAsync()
    {
        try
        {
            if (_composition is null || _lifetime.IsCancellationRequested)
            {
                return;
            }

            await _composition.TrustedModel.RefreshAsync(_role, _lifetime.Token);
            if (!_composition.TrustedModel.HasTrustedPeer && _role is DeviceRole.Child && _childModel is not null)
            {
                await _childModel.CreateInvitationAsync(_lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Activity завершается: отмена ожидаема.
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            SetLocalError(GetString(Resource.String.pairing_unexpected_error), GetErrorCode(exception));
        }
        finally
        {
            Render();
        }

        static string GetErrorCode(Exception exception) =>
            "pairing.failed." + exception.GetType().Name;
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (_destroyed || _lifetime.IsCancellationRequested)
        {
            return;
        }

        ClearLocalError();
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            // Отмена (уход с экрана/Stop) не является ошибкой пользователя.
        }
        catch (Exception exception) when (exception is not System.OperationCanceledException)
        {
            SetLocalError(GetString(Resource.String.pairing_unexpected_error), GetErrorCode(exception));
        }
        finally
        {
            Render();
        }

        static string GetErrorCode(Exception exception) => "pairing.failed." + exception.GetType().Name;
    }

    private async Task HandleScanButtonAsync()
    {
        if (_role is DeviceRole.Child)
        {
            if (_childModel is null)
            {
                return;
            }

            if (_childModel.State is PairingFlowState.WaitingForPeerResponse)
            {
                await StartCameraAsync();
                return;
            }

            await _childModel.CreateInvitationAsync(_lifetime.Token);
            return;
        }

        await StartCameraAsync();
    }

    private async Task StartCameraAsync()
    {
        if (_composition is null || _cameraPreview is null || _lifetime.IsCancellationRequested)
        {
            return;
        }

        var permission = await _composition.CameraPermission.EnsureGrantedAsync(_lifetime.Token);
        if (!permission.IsSuccess)
        {
            SetLocalError(permission.Error!.Message, permission.Error.Code);
            Render();
            return;
        }

        ClearLocalError();
        _cameraActive = true;
        Render();
        await _composition.Scanner.StartAsync(_cameraPreview, _lifetime.Token);
    }

    private void StopCamera(bool render)
    {
        _cameraActive = false;
        _composition?.Scanner.Stop();
        if (render)
        {
            Render();
        }
    }

    private async Task ApplyPastedPayloadAsync()
    {
        var payload = _paste?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(payload))
        {
            SetLocalError(GetString(Resource.String.pairing_paste_empty), "pairing.paste_empty");
            return;
        }

        // Поле очищается сразу: payload приглашения содержит одноразовый token (секрет).
        _paste!.Text = string.Empty;
        await RoutePayloadAsync(payload);
    }

    private async Task RoutePayloadAsync(string payload)
    {
        StopCamera(render: false);

        if (_role is DeviceRole.Child)
        {
            if (_childModel is not null)
            {
                await _childModel.AcceptResponseAsync(payload, _lifetime.Token);
            }

            return;
        }

        if (_parentModel is not null)
        {
            await _parentModel.AcceptInvitationAsync(payload, _lifetime.Token);
        }
    }

    private async Task ConfirmAsync(bool userConfirmed)
    {
        if (_role is DeviceRole.Child)
        {
            if (_childModel is not null)
            {
                await _childModel.ConfirmAsync(userConfirmed, _lifetime.Token);
            }
        }
        else if (_parentModel is not null)
        {
            await _parentModel.ConfirmAsync(userConfirmed, _lifetime.Token);
        }

        if (GetState() is PairingFlowState.Completed && _composition is not null)
        {
            await _composition.TrustedModel.RefreshAsync(_role, _lifetime.Token);
        }
    }

    private async Task StartRepairAsync()
    {
        if (_composition is null)
        {
            return;
        }

        _repairRequested = true;
        await ResetFlowModelAsync(createChildInvitation: true);
    }

    /// <summary>
    /// Пересоздаёт flow-модель (после «Сопрячь заново» и после успешного «Забыть устройство»):
    /// старые screen models освобождаются, чтобы экран вышел из терминального Completed/Failed
    /// и снова мог начать сценарий. Child сразу получает новое приглашение; Parent показывает scan.
    /// </summary>
    private async Task ResetFlowModelAsync(bool createChildInvitation)
    {
        if (_composition is null)
        {
            return;
        }

        if (_role is DeviceRole.Child)
        {
            _childModel?.Changed -= _modelChangedHandler;
            _childModel?.Dispose();
            _childModel = _composition.CreateChildModel();
            _childModel.Changed += _modelChangedHandler;
            if (createChildInvitation)
            {
                await _childModel.CreateInvitationAsync(_lifetime.Token);
            }
        }
        else
        {
            _parentModel?.Changed -= _modelChangedHandler;
            _parentModel?.Dispose();
            _parentModel = _composition.CreateParentModel();
            _parentModel.Changed += _modelChangedHandler;
        }
    }

    private void ShowForgetConfirmation()
    {
        if (_composition is null)
        {
            return;
        }

        var device = _composition.TrustedModel.Devices.FirstOrDefault();
        if (device is null)
        {
            return;
        }

        var builder = new AlertDialog.Builder(this);
        builder.SetTitle(Resource.String.pairing_forget_confirm_title);
        builder.SetMessage(Resource.String.pairing_forget_confirm_message);
        builder.SetPositiveButton(Resource.String.pairing_forget_confirm_positive, (_, _) => BeginForget(device.FingerprintSha256));
        builder.SetNegativeButton(Resource.String.pairing_forget_confirm_negative, (_, _) => { });
        builder.Show();
    }

    private async void BeginForget(string fingerprintSha256)
    {
        try
        {
            await ForgiveAsync(fingerprintSha256);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            SetLocalError(GetString(Resource.String.pairing_unexpected_error), "pairing.failed." + exception.GetType().Name);
        }
        finally
        {
            Render();
        }
    }

    private async Task ForgiveAsync(string fingerprintSha256)
    {
        await ExecuteAsync(async () =>
        {
            if (_composition is null)
            {
                return;
            }

            await _composition.TrustedModel.ForgetAsync(fingerprintSha256, _role, _lifetime.Token);
            if (_composition.TrustedModel.ErrorCode is null)
            {
                _repairRequested = false;
                await ResetFlowModelAsync(createChildInvitation: true);
            }
        });
    }

    private void OpenMonitoring()
    {
        var intent = new Intent(this, typeof(MonitoringActivity));
        intent.PutExtra(MonitoringActivity.ExtraRole, _role.ToString());
        StartActivity(intent);
        Finish();
    }

    private void OnScannerCodeDetected(object? sender, QrCameraScanner.QrCodeDetectedEventArgs args) =>
        RunOnUiThread(() => BeginConsume(args.Payload));

    private async void BeginConsume(string payload)
    {
        try
        {
            if (_destroyed)
            {
                return;
            }

            StopCamera(render: false);
            await RoutePayloadAsync(payload);
        }
        catch (OperationCanceledException)
        {
            // Уход с экрана/отмена запуска не является ошибкой пользователя.
        }
        catch (Exception exception)
        {
            SetLocalError(GetString(Resource.String.pairing_unexpected_error), "pairing.failed." + exception.GetType().Name);
        }
        finally
        {
            Render();
        }
    }

    private void OnScannerFailed(object? sender, QrCameraScanner.QrScannerFailedEventArgs args) =>
        RunOnUiThread(() =>
        {
            if (_destroyed)
            {
                return;
            }

            _cameraActive = false;
            SetLocalError(LocalizeScannerError(args.ErrorCode, args.Message), args.ErrorCode);
            Render();
        });

    private void StartExpiryTimer()
    {
        StopExpiryTimer();
        _expiryTimer = new Timer(_ => RunOnUiThread(TickExpiry), null, ExpiryTickMilliseconds, ExpiryTickMilliseconds);
    }

    private void StopExpiryTimer()
    {
        _expiryTimer?.Dispose();
        _expiryTimer = null;
    }

    private void TickExpiry()
    {
        if (_destroyed || _role is not DeviceRole.Child || _childModel is null)
        {
            return;
        }

        if (_childModel.State is PairingFlowState.WaitingForPeerResponse && !_childModel.IsBusy)
        {
            _childModel.PollExpiry();
        }

        RenderExpiry();
    }

    private void Render()
    {
        if (_destroyed || _composition is null)
        {
            return;
        }

        var state = GetState();
        var busy = IsBusy();
        var trusted = _composition.TrustedModel;

        _status!.Text = GetString(MapStatusString(state));
        _note!.Text = GetString(_role is DeviceRole.Child
            ? Resource.String.pairing_child_note
            : Resource.String.pairing_parent_note);
        _note.Visibility = state is PairingFlowState.Completed ? ViewStates.Gone : ViewStates.Visible;
        _progress!.Visibility = busy ? ViewStates.Visible : ViewStates.Gone;

        RenderError(state);

        var showTrusted = trusted.HasTrustedPeer
            && (state is PairingFlowState.Completed
                || (!_repairRequested && state is PairingFlowState.Idle or PairingFlowState.Failed));
        _trustedSection!.Visibility = showTrusted ? ViewStates.Visible : ViewStates.Gone;
        if (showTrusted)
        {
            var device = trusted.Devices.FirstOrDefault();
            if (device is not null)
            {
                _trustedInfo!.Text = string.Format(
                    CultureInfo.CurrentCulture,
                    GetString(Resource.String.pairing_trusted_info_format),
                    GetString(device.PeerRole is DeviceRole.Child
                        ? Resource.String.pairing_trusted_peer_child
                        : Resource.String.pairing_trusted_peer_parent),
                    device.FingerprintShort,
                    device.PairedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture));
            }
        }

        var showChildSection = _role is DeviceRole.Child
            && state is PairingFlowState.WaitingForPeerResponse
            && _childModel?.QrPayload is not null;
        _childSection!.Visibility = showChildSection ? ViewStates.Visible : ViewStates.Gone;
        if (showChildSection)
        {
            if (!SetQrImage(_qrImage!, _childModel!.QrPayload, ref _renderedQrPayload, ref _qrBitmap))
            {
                SetLocalError(GetString(Resource.String.pairing_unexpected_error), QrRenderFailedErrorCode);
            }

            RenderExpiry();
        }
        else
        {
            SetQrImage(_qrImage!, null, ref _renderedQrPayload, ref _qrBitmap);
        }

        var showParentResponse = _role is DeviceRole.Parent
            && state is PairingFlowState.AwaitingUserConfirmation
            && _parentModel?.ResponseQrPayload is not null;
        _parentSection!.Visibility = _role is DeviceRole.Parent ? ViewStates.Visible : ViewStates.Gone;
        _responseNote!.Visibility = showParentResponse ? ViewStates.Visible : ViewStates.Gone;
        _responseQrImage!.Visibility = showParentResponse ? ViewStates.Visible : ViewStates.Gone;
        if (!SetQrImage(
                _responseQrImage!,
                showParentResponse ? _parentModel!.ResponseQrPayload : null,
                ref _renderedResponsePayload,
                ref _responseQrBitmap)
            && showParentResponse)
        {
            SetLocalError(GetString(Resource.String.pairing_unexpected_error), QrRenderFailedErrorCode);
        }

        var hasTrustedPeer = trusted.HasTrustedPeer && !_repairRequested;
        var showScanControls = !_cameraActive
            && !(hasTrustedPeer && state is PairingFlowState.Idle or PairingFlowState.Failed)
            && (state is PairingFlowState.Failed
                || (state is PairingFlowState.WaitingForPeerResponse && _role is DeviceRole.Child)
                || (state is PairingFlowState.Idle && _role is DeviceRole.Parent));
        _scanControls!.Visibility = showScanControls ? ViewStates.Visible : ViewStates.Gone;
        if (showScanControls)
        {
            _scan!.Text = GetString(_role is DeviceRole.Child
                ? state is PairingFlowState.WaitingForPeerResponse
                    ? Resource.String.pairing_scan_child_response
                    : Resource.String.pairing_new_invitation
                : state is PairingFlowState.Failed
                    ? Resource.String.pairing_scan_again
                    : Resource.String.pairing_scan_parent_invitation);
            _scan.Enabled = !busy;
            _pasteApply!.Enabled = !busy;
        }

        var showVerification = state is PairingFlowState.AwaitingUserConfirmation;
        _verificationSection!.Visibility = showVerification ? ViewStates.Visible : ViewStates.Gone;
        if (showVerification)
        {
            var code = GetVerificationCode();
            _verificationCode!.Text = code;
            _verificationCode.ContentDescription = string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.pairing_code_cd),
                code);
            _confirm!.Enabled = !busy;
            _reject!.Enabled = !busy;
        }

        _cameraContainer!.Visibility = _cameraActive ? ViewStates.Visible : ViewStates.Gone;
        _cameraCancel!.Visibility = _cameraActive ? ViewStates.Visible : ViewStates.Gone;

        _skip!.Visibility = state is PairingFlowState.Completed || hasTrustedPeer
            ? ViewStates.Gone
            : ViewStates.Visible;
        _back!.Enabled = !busy;
    }

    private void RenderExpiry()
    {
        if (_expiry is null || _childModel is null)
        {
            return;
        }

        var remaining = _childModel.RemainingValidity;
        _expiry.Text = remaining is { } value
            ? string.Format(
                CultureInfo.CurrentCulture,
                GetString(Resource.String.pairing_expiry_format),
                value.ToString(@"m\:ss", CultureInfo.InvariantCulture))
            : string.Empty;
    }

    private void RenderError(PairingFlowState state)
    {
        var code = _localErrorCode ?? GetModelErrorCode();
        var message = _localErrorMessage ?? GetModelErrorMessage();
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(message))
        {
            _error!.Visibility = ViewStates.Gone;
            return;
        }

        _error!.Visibility = ViewStates.Visible;
        _error.Text = string.Format(
            CultureInfo.CurrentCulture,
            GetString(Resource.String.pairing_error_format),
            code ?? state.ToString(),
            message ?? GetString(Resource.String.pairing_unexpected_error));
    }

    private PairingFlowState GetState() => _role is DeviceRole.Child
        ? _childModel?.State ?? PairingFlowState.Idle
        : _parentModel?.State ?? PairingFlowState.Idle;

    private bool IsBusy() =>
        (_childModel?.IsBusy ?? false)
        || (_parentModel?.IsBusy ?? false)
        || (_composition?.TrustedModel.IsBusy ?? false);

    private string GetVerificationCode() => _role is DeviceRole.Child
        ? _childModel?.VerificationCode ?? string.Empty
        : _parentModel?.VerificationCode ?? string.Empty;

    private string? GetModelErrorCode() => _role is DeviceRole.Child
        ? _childModel?.ErrorCode
        : _parentModel?.ErrorCode;

    private string? GetModelErrorMessage() => _role is DeviceRole.Child
        ? _childModel?.ErrorMessage
        : _parentModel?.ErrorMessage;

    private void SetLocalError(string message, string code)
    {
        _localErrorCode = code;
        _localErrorMessage = message;
    }

    private void ClearLocalError()
    {
        _localErrorCode = null;
        _localErrorMessage = null;
    }

    private static bool SetQrImage(
        ImageView imageView,
        string? payload,
        ref string? renderedPayload,
        ref Android.Graphics.Bitmap? bitmap)
    {
        if (string.Equals(renderedPayload, payload, StringComparison.Ordinal))
        {
            return bitmap is not null;
        }

        renderedPayload = payload;
        bitmap?.Recycle();
        bitmap = null;
        if (string.IsNullOrWhiteSpace(payload))
        {
            imageView.SetImageDrawable(null);
            return true;
        }

        bitmap = QrCodeBitmapRenderer.TryCreate(payload, QrSizePixels);
        if (bitmap is null)
        {
            imageView.SetImageDrawable(null);
            return false;
        }

        imageView.SetImageBitmap(bitmap);
        return true;
    }

    private string LocalizeScannerError(string errorCode, string fallbackMessage) =>
        string.Equals(errorCode, QrScannerErrorCodes.CameraPermissionDenied, StringComparison.Ordinal)
            ? GetString(Resource.String.pairing_no_camera_permission)
            : fallbackMessage;

    private static int MapStatusString(PairingFlowState state) => state switch
    {
        PairingFlowState.WaitingForPeerResponse => Resource.String.pairing_state_waiting,
        PairingFlowState.AwaitingUserConfirmation => Resource.String.pairing_state_confirm,
        PairingFlowState.Completed => Resource.String.pairing_state_completed,
        PairingFlowState.Failed => Resource.String.pairing_state_failed,
        _ => Resource.String.pairing_state_idle
    };
}
