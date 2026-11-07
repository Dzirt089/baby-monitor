using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Presentation-agnostic модель экрана Parent для офлайн-сопряжения по QR: принимает QR-payload
/// приглашения Child, строит QR-ответ Parent и показывает verification code до подтверждения.
/// Не содержит Android-типов; QR-payload приглашения и ответа — секреты, которые не логируются.
/// </summary>
/// <remarks>
/// Все операции сериализованы общим семафором, событие <see cref="Changed"/> поднимается после
/// освобождения блокировки. После <see cref="Dispose"/> асинхронные методы не бросают, а
/// переводят модель в Failed с invalid_state.
/// </remarks>
public sealed class ParentPairingScreenModel : IDisposable
{
    private const int ShortFingerprintLength = 8;

    private readonly IPairingKeyStore _keyStore;
    private readonly ITrustedDeviceStore _trustedDeviceStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ParentPairingSession? _session;
    private PairingVerification? _verification;
    private PairingFlowState _state = PairingFlowState.Idle;
    private string? _responseQrPayload;
    private string? _verificationCode;
    private string? _childFingerprintShort;
    private string? _errorCode;
    private string? _errorMessage;
    private PairedDevice? _pairedDevice;
    private bool _isBusy;
    private int _disposed;

    /// <summary>Создаёт модель экрана сопряжения Parent.</summary>
    /// <param name="keyStore">Хранилище identity и операция подписи (Android Keystore в production).</param>
    /// <param name="trustedDeviceStore">Хранилище доверенных устройств.</param>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <exception cref="ArgumentNullException">Любой аргумент равен <see langword="null"/>.</exception>
    public ParentPairingScreenModel(
        IPairingKeyStore keyStore,
        ITrustedDeviceStore trustedDeviceStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(trustedDeviceStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _keyStore = keyStore;
        _trustedDeviceStore = trustedDeviceStore;
        _timeProvider = timeProvider;
    }

    /// <summary>Текущая фаза сценария сопряжения.</summary>
    public PairingFlowState State => _state;

    /// <summary>Verification code для сравнения пользователями; <see langword="null"/> до принятия приглашения.</summary>
    public string? VerificationCode => _verificationCode;

    /// <summary>
    /// QR-payload ответа Parent для показа/рендеринга. Секрет: не логируется.
    /// <see langword="null"/>, пока приглашение не принято.
    /// </summary>
    public string? ResponseQrPayload => _responseQrPayload;

    /// <summary>
    /// Первые 8 hex-символов fingerprint Child (lowercase). <see langword="null"/> до принятия
    /// приглашения; полный fingerprint для UI не нужен и не раскрывается.
    /// </summary>
    public string? ChildFingerprintShort => _childFingerprintShort;

    /// <summary>Стабильный код последней ошибки (без секретов); <see langword="null"/> при успехе.</summary>
    public string? ErrorCode => _errorCode;

    /// <summary>Безопасное для показа сообщение последней ошибки; <see langword="null"/> при успехе.</summary>
    public string? ErrorMessage => _errorMessage;

    /// <summary>Идёт асинхронная операция.</summary>
    public bool IsBusy => _isBusy;

    /// <summary>Сохранённое доверенное устройство; <see langword="null"/>, пока сопряжение не завершено.</summary>
    public PairedDevice? PairedDevice => _pairedDevice;

    /// <summary>Уведомление об изменении наблюдаемого состояния.</summary>
    public event EventHandler? Changed;

    /// <summary>Принимает QR-payload приглашения Child и формирует QR-ответ Parent.</summary>
    /// <param name="invitationQrPayload">QR-payload приглашения (может быть <see langword="null"/> при вводе вручную).</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <remarks>
    /// Разрешено из <see cref="PairingFlowState.Idle"/> или <see cref="PairingFlowState.Failed"/>:
    /// повторная попытка после ошибки допустима и сбрасывает ошибку.
    /// </remarks>
    public async Task AcceptInvitationAsync(string? invitationQrPayload, CancellationToken cancellationToken)
    {
        var shouldNotify = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                ApplyDisposedFailure();
                shouldNotify = true;
                return;
            }

            if (_state is not (PairingFlowState.Idle or PairingFlowState.Failed))
            {
                SetError(PairingErrorCodes.InvalidState, "Приглашение можно принять только в начале сценария или после ошибки.");
                shouldNotify = true;
                return;
            }

            _isBusy = true;
            shouldNotify = true;
            ClearError();
            ReleaseSession();
            _responseQrPayload = null;
            _verificationCode = null;
            _childFingerprintShort = null;
            try
            {
                var accepted = await ParentPairingSession.AcceptAsync(invitationQrPayload, _keyStore, _timeProvider, cancellationToken);
                if (accepted.IsSuccess)
                {
                    _session = accepted.Value;
                    _verification = _session.Verification;
                    _responseQrPayload = PairingResponseQrCodec.Encode(_session.Response);
                    _verificationCode = _verification.Code;
                    _childFingerprintShort = ToShortFingerprint(_session.Invitation.ChildIdentity.FingerprintSha256);
                    _state = PairingFlowState.AwaitingUserConfirmation;
                }
                else
                {
                    _state = PairingFlowState.Failed;
                    SetError(accepted.Error!);
                }
            }
            finally
            {
                _isBusy = false;
            }
        }
        finally
        {
            _gate.Release();
            if (shouldNotify)
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>Подтверждает (или отклоняет) сопряжение и сохраняет доверенное устройство.</summary>
    /// <param name="userConfirmed">Пользователь подтвердил совпадение verification code.</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <remarks>
    /// Разрешено только в <see cref="PairingFlowState.AwaitingUserConfirmation"/>. Отказ пользователя и
    /// несовпадение кода переводят модель в Failed; сбой trusted store оставляет состояние и позволяет
    /// повторить подтверждение.
    /// </remarks>
    public async Task ConfirmAsync(bool userConfirmed, CancellationToken cancellationToken)
    {
        var shouldNotify = false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _disposed) == 1)
            {
                ApplyDisposedFailure();
                shouldNotify = true;
                return;
            }

            if (_state is not PairingFlowState.AwaitingUserConfirmation || _session is null || _verification is null)
            {
                SetError(PairingErrorCodes.InvalidState, "Подтверждение доступно только после проверки verification code.");
                shouldNotify = true;
                return;
            }

            _isBusy = true;
            shouldNotify = true;
            try
            {
                var confirmed = await _session.ConfirmAsync(_verification, userConfirmed, _trustedDeviceStore, cancellationToken);
                if (confirmed.IsSuccess)
                {
                    _pairedDevice = confirmed.Value;
                    _state = PairingFlowState.Completed;
                    ClearError();
                }
                else
                {
                    var error = confirmed.Error!;
                    SetError(error);
                    if (error.Code != PairingErrorCodes.StoreFailed)
                    {
                        _state = PairingFlowState.Failed;
                    }
                }
            }
            finally
            {
                _isBusy = false;
            }
        }
        finally
        {
            _gate.Release();
            if (shouldNotify)
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>
    /// Отменяет сценарий пользователем: освобождает сессию и переводит модель в Failed с
    /// pairing.cancelled. Ничего не делает, если сценарий уже завершён или модель освобождена.
    /// </summary>
    public void Cancel()
    {
        var shouldNotify = false;
        // Не блокируем UI-поток: во время активной операции отмена не применяется (кнопки disabled).
        if (!_gate.Wait(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _disposed) == 1 || _state is PairingFlowState.Completed)
            {
                return;
            }

            ReleaseSession();
            _state = PairingFlowState.Failed;
            SetError(PairingErrorCodes.Cancelled, "Сопряжение отменено пользователем.");
            shouldNotify = true;
        }
        finally
        {
            _gate.Release();
            if (shouldNotify)
            {
                RaiseChanged();
            }
        }
    }

    /// <summary>
    /// Освобождает pairing-сессию и эфемерные ключи. Идемпотентно; после Dispose публичные
    /// асинхронные методы не бросают, а переводят модель в Failed с invalid_state.
    /// </summary>
    /// <remarks>
    /// Dispose не блокирует UI-поток и не берёт семафор: он выставляет disposed-флаг первым, поэтому
    /// конкурентные публичные операции завершаются invalid_state. Семафор не освобождается: без
    /// WaitHandle он не держит неуправляемых ресурсов, а его освобождение сделало бы вызовы после
    /// Dispose бросающими ObjectDisposedException.
    /// </remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        var session = Interlocked.Exchange(ref _session, null);
        session?.Dispose();
        _verification = null;
    }

    private void ReleaseSession()
    {
        _session?.Dispose();
        _session = null;
        _verification = null;
    }

    private void ApplyDisposedFailure()
    {
        _state = PairingFlowState.Failed;
        SetError(PairingErrorCodes.InvalidState, "Pairing-сессия уже освобождена.");
    }

    private void SetError(Error error)
    {
        _errorCode = error.Code;
        _errorMessage = error.Message;
    }

    private void SetError(string code, string message)
    {
        _errorCode = code;
        _errorMessage = message;
    }

    private void ClearError()
    {
        _errorCode = null;
        _errorMessage = null;
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static string ToShortFingerprint(string fingerprintSha256) =>
        (fingerprintSha256.Length <= ShortFingerprintLength ? fingerprintSha256 : fingerprintSha256[..ShortFingerprintLength])
        .ToLowerInvariant();
}
