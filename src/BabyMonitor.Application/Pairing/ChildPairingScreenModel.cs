using BabyMonitor.Application.Abstractions.Security;
using BabyMonitor.Application.Results;

namespace BabyMonitor.Application.Pairing;

/// <summary>
/// Presentation-agnostic модель экрана Child для офлайн-сопряжения по QR: создаёт приглашение,
/// принимает QR-ответ Parent, показывает verification code и сохраняет доверие после
/// подтверждения пользователя. Не содержит Android-типов; QR-payload приглашения и ответа —
/// секреты, которые не логируются, а приватные ключи живут только в <see cref="IPairingKeyStore"/>.
/// </summary>
/// <remarks>
/// Все операции сериализованы общим семафором, событие <see cref="Changed"/> поднимается после
/// освобождения блокировки. После <see cref="Dispose"/> асинхронные методы не бросают, а
/// переводят модель в Failed с invalid_state.
/// </remarks>
public sealed class ChildPairingScreenModel : IDisposable
{
    private const int ShortFingerprintLength = 8;

    private readonly IPairingKeyStore _keyStore;
    private readonly IPairingEntropy _entropy;
    private readonly ITrustedDeviceStore _trustedDeviceStore;
    private readonly TimeProvider _timeProvider;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ChildPairingSession? _session;
    private PairingVerification? _verification;
    private PairingFlowState _state = PairingFlowState.Idle;
    private string? _qrPayload;
    private DateTimeOffset? _expiresAt;
    private string? _verificationCode;
    private string? _peerFingerprintShort;
    private string? _errorCode;
    private string? _errorMessage;
    private PairedDevice? _pairedDevice;
    private bool _isBusy;
    private int _disposed;

    /// <summary>Создаёт модель экрана сопряжения Child.</summary>
    /// <param name="keyStore">Хранилище identity и операция подписи (Android Keystore в production).</param>
    /// <param name="entropy">Источник криптостойкой случайности для pairing token.</param>
    /// <param name="trustedDeviceStore">Хранилище доверенных устройств.</param>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <exception cref="ArgumentNullException">Любой аргумент равен <see langword="null"/>.</exception>
    public ChildPairingScreenModel(
        IPairingKeyStore keyStore,
        IPairingEntropy entropy,
        ITrustedDeviceStore trustedDeviceStore,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(keyStore);
        ArgumentNullException.ThrowIfNull(entropy);
        ArgumentNullException.ThrowIfNull(trustedDeviceStore);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _keyStore = keyStore;
        _entropy = entropy;
        _trustedDeviceStore = trustedDeviceStore;
        _timeProvider = timeProvider;
    }

    /// <summary>Текущая фаза сценария сопряжения.</summary>
    public PairingFlowState State => _state;

    /// <summary>
    /// QR-payload приглашения для показа/рендеринга. Секрет: не логируется. <see langword="null"/>,
    /// пока приглашение не создано.
    /// </summary>
    public string? QrPayload => _qrPayload;

    /// <summary>Момент истечения приглашения; <see langword="null"/>, пока приглашение не создано.</summary>
    public DateTimeOffset? ExpiresAt => _expiresAt;

    /// <summary>
    /// Оставшееся время жизни приглашения, не меньше <see cref="TimeSpan.Zero"/>.
    /// <see langword="null"/>, если приглашения нет.
    /// </summary>
    public TimeSpan? RemainingValidity
    {
        get
        {
            if (_expiresAt is not { } expiresAt)
            {
                return null;
            }

            var remaining = expiresAt - _timeProvider.GetUtcNow();
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>Verification code для сравнения пользователями; <see langword="null"/> до ответа Parent.</summary>
    public string? VerificationCode => _verificationCode;

    /// <summary>
    /// Первые 8 hex-символов fingerprint Parent (lowercase). <see langword="null"/> до ответа Parent;
    /// полный fingerprint для UI не нужен и не раскрывается.
    /// </summary>
    public string? PeerFingerprintShort => _peerFingerprintShort;

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

    /// <summary>Создаёт новое одноразовое приглашение и QR-payload приглашения.</summary>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <remarks>
    /// Разрешено только из <see cref="PairingFlowState.Idle"/> или <see cref="PairingFlowState.Failed"/>.
    /// В остальных состояниях выставляет invalid_state без изменения состояния.
    /// </remarks>
    public async Task CreateInvitationAsync(CancellationToken cancellationToken)
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
                SetError(PairingErrorCodes.InvalidState, "Приглашение можно создать только в начале сценария или после ошибки.");
                shouldNotify = true;
                return;
            }

            _isBusy = true;
            shouldNotify = true;
            ClearError();
            ReleaseSession();
            _qrPayload = null;
            _expiresAt = null;
            _verificationCode = null;
            _peerFingerprintShort = null;
            try
            {
                var created = await ChildPairingSession.CreateAsync(_keyStore, _entropy, _timeProvider, cancellationToken);
                if (created.IsSuccess)
                {
                    _session = created.Value;
                    _state = PairingFlowState.WaitingForPeerResponse;
                    _qrPayload = _session.QrPayload;
                    _expiresAt = _session.Invitation.ExpiresAt;
                }
                else
                {
                    _state = PairingFlowState.Failed;
                    SetError(created.Error!);
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
    /// Принимает QR-payload ответа Parent, проверяет его и получает verification code.
    /// </summary>
    /// <param name="responseQrPayload">QR-payload ответа Parent (может быть <see langword="null"/> при вводе вручную).</param>
    /// <param name="cancellationToken">Отмена операции.</param>
    /// <remarks>
    /// Разрешено только в <see cref="PairingFlowState.WaitingForPeerResponse"/>. Ошибка разбора QR и
    /// ошибка подписи оставляют состояние WaitingForPeerResponse (повтор возможен); expired,
    /// token_reused и invalid_state переводят модель в Failed.
    /// </remarks>
    public async Task AcceptResponseAsync(string? responseQrPayload, CancellationToken cancellationToken)
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

            if (_state is not PairingFlowState.WaitingForPeerResponse || _session is null)
            {
                SetError(PairingErrorCodes.InvalidState, "Ответ родителя можно принять только после создания приглашения.");
                shouldNotify = true;
                return;
            }

            _isBusy = true;
            shouldNotify = true;
            try
            {
                var decoded = PairingResponseQrCodec.Decode(responseQrPayload);
                if (!decoded.IsSuccess)
                {
                    SetError(decoded.Error!);
                    return;
                }

                var handled = await _session.HandleResponseAsync(decoded.Value, cancellationToken);
                if (handled.IsSuccess)
                {
                    _verification = handled.Value;
                    _verificationCode = handled.Value.Code;
                    _peerFingerprintShort = ToShortFingerprint(decoded.Value.ParentIdentity.FingerprintSha256);
                    _state = PairingFlowState.AwaitingUserConfirmation;
                    ClearError();
                }
                else
                {
                    SetError(handled.Error!);
                    if (handled.Error!.Code is PairingErrorCodes.Expired
                        or PairingErrorCodes.TokenReused
                        or PairingErrorCodes.InvalidState)
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
    /// Проверяет истечение приглашения; вызывается UI-таймером. Идемпотентно: повторный вызов
    /// после перехода в Failed не меняет состояние и не поднимает <see cref="Changed"/>.
    /// </summary>
    public void PollExpiry()
    {
        var shouldNotify = false;
        // Не блокируем UI-поток: если операция в полёте, таймер повторит попытку через секунду.
        if (!_gate.Wait(TimeSpan.Zero))
        {
            return;
        }

        try
        {
            if (Volatile.Read(ref _disposed) == 1
                || _state is not PairingFlowState.WaitingForPeerResponse
                || _expiresAt is not { } expiresAt
                || _timeProvider.GetUtcNow() <= expiresAt)
            {
                return;
            }

            ReleaseSession();
            _state = PairingFlowState.Failed;
            SetError(PairingErrorCodes.Expired, "Срок действия приглашения истёк. Создайте новое приглашение.");
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
