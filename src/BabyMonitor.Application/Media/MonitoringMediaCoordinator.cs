using BabyMonitor.Application.Abstractions.Audio;
using BabyMonitor.Application.Abstractions.Connectivity;
using BabyMonitor.Application.Abstractions.Discovery;
using BabyMonitor.Application.Abstractions.Realtime;
using BabyMonitor.Application.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Application.Media;

/// <summary>
/// Presentation-agnostic координатор media-сеанса наблюдения (005 phase 2d): связывает
/// discovery/advertising, signaling/media-порты и audio-порты в один управляемый сеанс,
/// выполняет ограниченное переподключение по <see cref="ReconnectPolicy"/> и публикует
/// immutable <see cref="MediaSessionSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Роли строго асимметричны. Child: signaling listener → NSD advertisement → приём доверенного
/// peer → захват микрофона → Opus encode → отправка кадров в transport. Parent: discovery →
/// подключение к найденному Child → воспроизведение → приём кадров → Opus decode → запись в
/// playback. Child не создаёт playback, Parent не создаёт capture.
/// </para>
/// <para>
/// Доверие к peer и pinning настраиваются composition root-ом внутри adapters; координатор
/// принимает endpoint только из discovery и не подменяет identity. Полный fingerprint peer
/// в снимки не попадает: только первые 8 hex-символов, если composition root передал его
/// исключительно для отображения.
/// </para>
/// <para>
/// Application не логирует, не видит Android/SIPSorcery/JSON и не хранит аудиоданные дольше
/// одного кадра. Все циклы привязаны к cancellation token; run task принадлежит координатору и
/// ожидается в StopAsync/DisposeAsync.
/// </para>
/// <para>
/// Control и media health проверяются независимо (FR-001/FR-002 007). Heartbeat приходит из
/// <see cref="IRealtimeTransportSession.HeartbeatReceived"/> (pong) и обновляет
/// <see cref="SessionHealthMonitor"/>; отсутствие первого heartbeat или потеря контрольного канала
/// ведут к typed <see cref="RealtimeTransportErrorCodes.HeartbeatMissed"/> и Degraded/Reconnecting.
/// Media health оценивается watchdog-таймером TimeProvider (stall media flow → Degraded/Reconnecting).
/// ParentLossAlarm для Parent поднимается локально по TimeProvider (независимо от ответа Child);
/// её состояние публикуется в <see cref="MediaSessionSnapshot.ParentAlarmActive"/> без смены State.
/// </para>
/// <para>
/// Владение ресурсами: координатор освобождает сессии, которые сам создал (listener, advertiser,
/// discovery, transport, capture, playback), но не освобождает инжектированные порты-фабрики и
/// <see cref="IConnectivityMonitor"/> — их владелец composition root (foreground service).
/// </para>
/// </remarks>
public sealed class MonitoringMediaCoordinator : IDisposable, IAsyncDisposable
{
    private const int ShortFingerprintLength = 8;
    private const int Sha256HexLength = 64;

    private static readonly TimeSpan MediaWatchdogPeriod = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan AlarmWatchdogPeriod = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan LiveSnapshotRefreshPeriod = TimeSpan.FromSeconds(1);

    private readonly object _sync = new();
    private readonly DeviceRole _role;
    private readonly IPeerDiscovery _discovery;
    private readonly IPeerAdvertiser _advertiser;
    private readonly IRealtimeTransport _transport;
    private readonly IRealtimeTransportListener _listener;
    private readonly IAudioCapture _capture;
    private readonly IAudioPlayback _playback;
    private readonly IAudioCodec _codec;
    private readonly IConnectivityMonitor _connectivity;
    private readonly ReconnectPolicy _reconnectPolicy;
    private readonly TimeSpan _reconnectAttemptTimeout;
    private readonly TimeProvider _timeProvider;
    private readonly SessionHealthMonitor _healthMonitor;
    private readonly ParentLossAlarm _parentLossAlarm;
    private readonly string? _peerFingerprintShort;

    private MediaSessionSnapshot? _snapshot;
    private MonitoringSession? _session;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _attemptCts;
    private Task? _runTask;
    private TaskCompletionSource<Result<MediaSessionSnapshot>>? _startTcs;
    private bool _stopRequested;
    private bool _disposed;
    private RealtimeTransportState? _transportState;
    private bool _captureActive;
    private bool _playbackActive;
    private bool _mediaAlarmActive;
    private long _attemptGeneration;
    private long _sentFrames;
    private long _receivedFrames;
    private int _reconnectAttempts;
    private long _lastMediaFrameAtUnixMilliseconds = -1;
    private DateTimeOffset _telemetryStartedAt;

    /// <summary>Создаёт координатор media-сеанса для одной роли.</summary>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <param name="discovery">Порт поиска Child (используется только ролью Parent).</param>
    /// <param name="advertiser">Порт публикации Child (используется только ролью Child).</param>
    /// <param name="transport">Порт transport-соединения; identity peer настраивается composition root-ом.</param>
    /// <param name="listener">Порт ожидания доверенного peer; identity peer настраивается composition root-ом.</param>
    /// <param name="capture">Порт захвата микрофона (только Child).</param>
    /// <param name="playback">Порт воспроизведения (только Parent).</param>
    /// <param name="codec">Кодек Opus: encode для Child, decode для Parent.</param>
    /// <param name="connectivity">Порт network callbacks: смена сети перезапускает устаревшие операции.</param>
    /// <param name="reconnectPolicy">Единая политика backoff для автоматического переподключения.</param>
    /// <param name="timeProvider">Инжектированное время (fake в тестах).</param>
    /// <param name="peerFingerprintSha256">
    /// Полный SHA-256 fingerprint доверенного peer из composition root-а. Используется только для
    /// отображения (в снимок попадают первые 8 hex-символов) и не влияет на pinning/identity:
    /// решение о доверии принимает adapter. null — fingerprint неизвестен.
    /// </param>
    /// <param name="heartbeatTimeout">
    /// Порог потери control heartbeat для <see cref="SessionHealthMonitor"/> (heartbeat watchdog).
    /// null — default монитора (10 s). Composition root передаёт значение transport options
    /// (например, 15 s), чтобы coordinator и transport использовали единый порог; иначе watchdog
    /// мог бы сработать раньше/позже транспортного heartbeat-контроля.
    /// </param>
    /// <param name="parentAlarmThreshold">
    /// Порог локальной тревоги Parent по отсутствию heartbeat/media. null — default
    /// <see cref="ParentLossAlarm"/> (15 s).
    /// </param>
    /// <param name="reconnectAttemptTimeout">
    /// Таймаут setup-фазы попыток переподключения (retryIndex &gt; 0): discovery/accept/connect.
    /// null — 20 s. По истечении попытка завершается typed
    /// <see cref="RealtimeTransportErrorCodes.SignalingTimeout"/>, чтобы MaxAttempts исчерпывался и
    /// сеанс уходил в terminal Failed. Первичная попытка (retryIndex = 0) этим таймаутом не
    /// ограничивается: пользователь может искать Child долго.
    /// </param>
    /// <exception cref="ArgumentException">Роль Unknown/неизвестна или fingerprint некорректен.</exception>
    /// <exception cref="ArgumentNullException">Любой обязательный порт/политика/время равны null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Пороги heartbeat/alarm не положительны.</exception>
    public MonitoringMediaCoordinator(
        DeviceRole role,
        IPeerDiscovery discovery,
        IPeerAdvertiser advertiser,
        IRealtimeTransport transport,
        IRealtimeTransportListener listener,
        IAudioCapture capture,
        IAudioPlayback playback,
        IAudioCodec codec,
        IConnectivityMonitor connectivity,
        ReconnectPolicy reconnectPolicy,
        TimeProvider timeProvider,
        string? peerFingerprintSha256 = null,
        TimeSpan? heartbeatTimeout = null,
        TimeSpan? parentAlarmThreshold = null,
        TimeSpan? reconnectAttemptTimeout = null)
    {
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Координатор media-сеанса требует роль Child или Parent.", nameof(role));
        }

        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(advertiser);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(listener);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(codec);
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(reconnectPolicy);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (reconnectAttemptTimeout is { } attemptTimeout && attemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(reconnectAttemptTimeout),
                attemptTimeout,
                "Таймаут попытки переподключения должен быть больше нуля.");
        }

        if (peerFingerprintSha256 is not null)
        {
            if (peerFingerprintSha256.Length != Sha256HexLength || !peerFingerprintSha256.All(char.IsAsciiHexDigit))
            {
                throw new ArgumentException(
                    "Fingerprint peer должен быть SHA-256 в hex (64 символа).",
                    nameof(peerFingerprintSha256));
            }

            _peerFingerprintShort = peerFingerprintSha256[..ShortFingerprintLength].ToLowerInvariant();
        }

        _role = role;
        _discovery = discovery;
        _advertiser = advertiser;
        _transport = transport;
        _listener = listener;
        _capture = capture;
        _playback = playback;
        _codec = codec;
        _connectivity = connectivity;
        _reconnectPolicy = reconnectPolicy;
        _reconnectAttemptTimeout = reconnectAttemptTimeout ?? TimeSpan.FromSeconds(20);
        _timeProvider = timeProvider;
        _healthMonitor = new SessionHealthMonitor(timeProvider, heartbeatTimeout: heartbeatTimeout);
        _parentLossAlarm = new ParentLossAlarm(timeProvider, parentAlarmThreshold);
        _parentLossAlarm.AlarmChanged += OnParentAlarmChanged;
        _connectivity.NetworkChanged += OnNetworkChanged;
    }

    /// <summary>Роль локального устройства, для которой создан координатор.</summary>
    public DeviceRole Role => _role;

    /// <summary>
    /// Последний снимок media-сеанса; null до первого <see cref="StartAsync"/>. Health внутри
    /// снимка пересчитывается при чтении, чтобы presentation получал актуальную оценку.
    /// </summary>
    public MediaSessionSnapshot? Snapshot
    {
        get
        {
            lock (_sync)
            {
                return _snapshot is null ? null : CreateSnapshotLocked(_snapshot.State, _snapshot.Error);
            }
        }
    }

    /// <summary>Уведомление об изменении состояния media-сеанса (immutable снимок).</summary>
    public event EventHandler<MediaSessionSnapshot>? SnapshotChanged;

    /// <summary>
    /// Запускает media-сеанс. Успех возвращается, когда Child начал advertising (listener запущен,
    /// сервис опубликован) либо Parent начал discovery; дальнейший прогресс наблюдается через
    /// <see cref="SnapshotChanged"/>. Повторный Start во время активного сеанса идемпотентен.
    /// </summary>
    /// <param name="cancellationToken">Отмена ожидания готовности; отмена останавливает запущенный сеанс.</param>
    /// <returns>Снимок готовности либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Ожидание запуска отменено вызывающим кодом.</exception>
    public async Task<Result<MediaSessionSnapshot>> StartAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<Result<MediaSessionSnapshot>> startTcs;
        MediaSessionSnapshot? startingSnapshot = null;
        EventHandler<MediaSessionSnapshot>? startingHandler = null;

        lock (_sync)
        {
            if (_disposed)
            {
                return Result<MediaSessionSnapshot>.Failure(new Error(
                    MediaSessionErrorCodes.InvalidState,
                    "Координатор media-сеанса уже освобождён."));
            }

            if (_runTask is { IsCompleted: false })
            {
                if (_snapshot is { State: MediaSessionState.Stopping })
                {
                    return Result<MediaSessionSnapshot>.Failure(new Error(
                        MediaSessionErrorCodes.InvalidState,
                        "Media-сеанс останавливается; дождитесь состояния Stopped."));
                }

                if (_startTcs is null)
                {
                    return _snapshot is null
                        ? Result<MediaSessionSnapshot>.Failure(new Error(
                            MediaSessionErrorCodes.InvalidState,
                            "Состояние media-сеанса противоречиво: нет ни запуска, ни снимка."))
                        : Result<MediaSessionSnapshot>.Success(CreateSnapshotLocked(_snapshot.State, _snapshot.Error));
                }

                startTcs = _startTcs;
            }
            else
            {
                _stopRequested = false;
                _healthMonitor.Reset();
                _transportState = null;
                _captureActive = false;
                _playbackActive = false;
                _mediaAlarmActive = false;
                Interlocked.Exchange(ref _sentFrames, 0);
                Interlocked.Exchange(ref _receivedFrames, 0);
                Volatile.Write(ref _reconnectAttempts, 0);
                Interlocked.Exchange(ref _lastMediaFrameAtUnixMilliseconds, -1);
                _telemetryStartedAt = _timeProvider.GetUtcNow();
                _session = MonitoringSession.Create(Guid.NewGuid().ToString("N"), _role, _timeProvider);
                startTcs = new TaskCompletionSource<Result<MediaSessionSnapshot>>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                _startTcs = startTcs;
                _runCts = new CancellationTokenSource();
                var runToken = _runCts.Token;
                startingSnapshot = SetSnapshotLocked(MediaSessionState.Starting, null);
                startingHandler = SnapshotChanged;
                _runTask = Task.Run(() => RunAsync(runToken), CancellationToken.None);
            }
        }

        if (startingSnapshot is not null)
        {
            startingHandler?.Invoke(this, startingSnapshot);
        }

        try
        {
            return await startTcs.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Отмена ожидания Start означает, что вызывающий код больше не нуждается в сеансе:
            // не оставляем бесхозный фоновый запуск.
            await CancelRunAndWaitAsync();
            throw;
        }
    }

    /// <summary>
    /// Останавливает media-сеанс: отменяет reconnect loop, освобождает transport/listener/audio/
    /// discovery/advertiser и ожидает завершения принадлежащей координатору run task.
    /// </summary>
    /// <param name="cancellationToken">Ограничение ожидания остановки; отмена не оставляет сеанс в Stopping.</param>
    /// <returns>Терминальный снимок медиасеанса либо типизированная ошибка.</returns>
    /// <exception cref="OperationCanceledException">Ожидание остановки отменено вызывающим кодом.</exception>
    public async Task<Result<MediaSessionSnapshot>> StopAsync(CancellationToken cancellationToken)
    {
        Task? runTask;
        CancellationTokenSource? runCts;
        MediaSessionSnapshot? stoppingSnapshot = null;
        EventHandler<MediaSessionSnapshot>? stoppingHandler = null;
        var alreadyTerminal = false;

        lock (_sync)
        {
            if (_disposed)
            {
                return Result<MediaSessionSnapshot>.Failure(new Error(
                    MediaSessionErrorCodes.InvalidState,
                    "Координатор media-сеанса уже освобождён."));
            }

            if (_snapshot is null)
            {
                return Result<MediaSessionSnapshot>.Failure(new Error(
                    MediaSessionErrorCodes.InvalidState,
                    "Media-сеанс ещё не запускался."));
            }

            if (_snapshot.IsTerminal)
            {
                alreadyTerminal = true;
            }
            else if (!_stopRequested)
            {
                _stopRequested = true;

                if (_session is { } session && !session.IsTerminal && IsStoppableState(session.State))
                {
                    session.BeginStopping();
                }

                stoppingSnapshot = SetSnapshotLocked(MediaSessionState.Stopping, null);
                stoppingHandler = SnapshotChanged;
            }

            runTask = _runTask;
            runCts = _runCts;
        }

        if (stoppingSnapshot is not null)
        {
            stoppingHandler?.Invoke(this, stoppingSnapshot);
        }

        if (!alreadyTerminal)
        {
            CancelQuietly(runCts);
        }

        if (runTask is not null && !runTask.IsCompleted)
        {
            try
            {
                await runTask.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                // Run task отменён ручным Stop — штатное завершение.
            }
        }

        lock (_sync)
        {
            var snapshot = _snapshot;
            if (snapshot is null)
            {
                return Result<MediaSessionSnapshot>.Success(new MediaSessionSnapshot(_role, MediaSessionState.Stopped));
            }

            return Result<MediaSessionSnapshot>.Success(CreateSnapshotLocked(snapshot.State, snapshot.Error));
        }
    }

    /// <summary>
    /// Запрашивает остановку и освобождает подписку на network callbacks. Не блокирует ожидание
    /// завершения run task: для гарантированного освобождения ресурсов используйте
    /// <see cref="DisposeAsync"/>. Идемпотентно.
    /// </summary>
    public void Dispose()
    {
        CancellationTokenSource? runCts;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _stopRequested = true;
            runCts = _runCts;
            _startTcs?.TrySetResult(Result<MediaSessionSnapshot>.Failure(new Error(
                MediaSessionErrorCodes.InvalidState,
                "Координатор media-сеанса освобождён до готовности запуска.")));
        }

        _parentLossAlarm.AlarmChanged -= OnParentAlarmChanged;
        _connectivity.NetworkChanged -= OnNetworkChanged;
        CancelQuietly(runCts);
    }

    /// <summary>
    /// Освобождает координатор и ожидает завершения принадлежащего ему run task (все адаптеры
    /// освобождаются внутри run task). Идемпотентно; после вызова StartAsync/StopAsync возвращают
    /// типизированную ошибку invalid_state, исключения ObjectDisposedException не бросаются.
    /// </summary>
    /// <returns>Задача освобождения.</returns>
    public async ValueTask DisposeAsync()
    {
        Dispose();

        Task? runTask;
        lock (_sync)
        {
            runTask = _runTask;
        }

        if (runTask is not null)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // Run task завершился отменой — штатное завершение освобождения.
            }
        }
    }

    private async Task RunAsync(CancellationToken runToken)
    {
        Error? unexpected = null;
        ITimer? alarmTimer = null;
        if (_role is DeviceRole.Parent)
        {
            // Тревога Parent оценивается локально по TimeProvider независимо от media watchdog:
            // watchdog работает только внутри media loop, а потеря peer-а должна быть видна и во
            // время reconnect-попыток (FR-002 007).
            alarmTimer = _timeProvider.CreateTimer(
                _ => EvaluateParentAlarm(),
                null,
                AlarmWatchdogPeriod,
                AlarmWatchdogPeriod);
        }

        // Live-refresh снимка: telemetry/uptime/alarm меняются и без смены State, поэтому
        // диагностика должна получать актуальные counters, не дожидаясь transition (device-test v0.4.0).
        var liveRefreshTimer = _timeProvider.CreateTimer(
            _ => PublishLiveRefresh(),
            null,
            LiveSnapshotRefreshPeriod,
            LiveSnapshotRefreshPeriod);

        try
        {
            await RunSessionAsync(runToken);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            // Ручной Stop/Dispose отменил сеанс: штатное завершение.
        }
        catch (Exception exception)
        {
            unexpected = MapAdapterFailure(exception);
        }
        finally
        {
            alarmTimer?.Dispose();
            liveRefreshTimer.Dispose();
            await FinalizeRunAsync(unexpected);
        }
    }

    private async Task RunSessionAsync(CancellationToken runToken)
    {
        var retryIndex = 0;

        while (true)
        {
            runToken.ThrowIfCancellationRequested();

            if (retryIndex > 0)
            {
                await Task.Delay(_reconnectPolicy.GetDelay(retryIndex), _timeProvider, runToken);
                Interlocked.Increment(ref _reconnectAttempts);
            }

            var outcome = _role is DeviceRole.Child
                ? await RunChildAttemptAsync(runToken, retryIndex == 0)
                : await RunParentAttemptAsync(runToken, retryIndex == 0);

            if (outcome.Kind is AttemptKind.Canceled)
            {
                return;
            }

            if (outcome.Kind is AttemptKind.Stale)
            {
                // Сеть изменилась до установления сессии: повторяем setup без расхода попытки.
                continue;
            }

            if (outcome.Kind is AttemptKind.SetupFailure)
            {
                if (retryIndex == 0)
                {
                    FailStartup(outcome.Error!);
                    return;
                }

                retryIndex++;
                if (!_reconnectPolicy.CanAttempt(retryIndex))
                {
                    FailReconnect(outcome.Error!);
                    return;
                }

                continue;
            }

            // Первичный отказ самого аудио-устройства (микрофон/динамик/буфер) — это не сетевой
            // разрыв: сообщаем typed error, не запуская reconnect-цикл. Сетевые потери (включая
            // first-frame watchdog «звука нет») уходят в reconnect ниже.
            if (retryIndex == 0
                && !outcome.MediaFlowing
                && outcome.Error is { } mediaError
                && IsDeviceAudioFailure(mediaError))
            {
                FailStartup(mediaError);
                return;
            }

            // Сессия установилась (transport Connected) и потеряна — включая случай, когда первый
            // аудиокадр так и не пришёл (first-frame watchdog): это может быть сетевой media path,
            // поэтому применяем reconnect, а не терминальный Failed на первой же попытке.
            PublishDegraded(outcome.Reason);
            PublishReconnecting();
            retryIndex = outcome.MediaFlowing ? 1 : retryIndex + 1;
            if (!_reconnectPolicy.CanAttempt(retryIndex))
            {
                FailReconnect(outcome.Error!);
                return;
            }
        }
    }

    private async Task<AttemptOutcome> RunChildAttemptAsync(CancellationToken runToken, bool initial)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        var ct = attemptCts.Token;
        RegisterAttempt(attemptCts);
        var attemptGeneration = Interlocked.Increment(ref _attemptGeneration);

        IRealtimeTransportListenerSession? listener = null;
        IPeerAdvertiserSession? advertiser = null;
        IRealtimeTransportSession? transport = null;
        IAudioCaptureSession? capture = null;
        var heartbeatSeen = new bool[1];
        EventHandler? heartbeatHandler = null;
        var setupTimedOut = new bool[1];
        using CancellationTokenSource? setupTimeoutCts = initial
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        using ITimer? setupTimeoutTimer = setupTimeoutCts is null
            ? null
            : _timeProvider.CreateTimer(
                _ =>
                {
                    Volatile.Write(ref setupTimedOut[0], true);
                    CancelQuietly(setupTimeoutCts);
                },
                null,
                _reconnectAttemptTimeout,
                Timeout.InfiniteTimeSpan);
        var setupToken = setupTimeoutCts?.Token ?? ct;

        try
        {
            var listenerResult = await _listener.StartAsync(PeerDiscoveryConstants.ProvisionalTransportPort, setupToken);
            if (!listenerResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(listenerResult.Error!);
            }

            listener = listenerResult.Value;
            setupToken.ThrowIfCancellationRequested();

            if (listener.BoundPort != PeerDiscoveryConstants.ProvisionalTransportPort)
            {
                return AttemptOutcome.SetupFailure(new Error(
                    MediaSessionErrorCodes.InvalidState,
                    "Signaling listener занял порт, отличный от публикуемого discovery; Parent не сможет подключиться."));
            }

            var advertiserResult = await _advertiser.StartAsync(
                new PeerAdvertisement(DeviceRole.Child, PeerDiscoveryConstants.CurrentProtocolVersion),
                setupToken);
            if (!advertiserResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(advertiserResult.Error!);
            }

            advertiser = advertiserResult.Value;
            PublishAdvertising(initial);
            SignalStarted();
            PublishChildConnecting();

            var accepted = await listener.AcceptAsync(setupToken);
            if (!accepted.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(accepted.Error!);
            }

            transport = accepted.Value;
            // Heartbeat-подписка сразу после создания transport-сессии и до PublishConnected/capture:
            // первый pong не теряется в окне между handshake и запуском media loop (L7).
            _healthMonitor.Reset();
            heartbeatHandler = (_, _) => OnHeartbeat(heartbeatSeen);
            transport.HeartbeatReceived += heartbeatHandler;
            UpdateTransportState(transport.State);
            PublishConnected();

            var captureResult = await _capture.StartAsync(new AudioCaptureOptions(_codec.Format), ct);
            if (!captureResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(captureResult.Error!);
            }

            capture = captureResult.Value;
            SetCaptureActive(true);
            setupTimeoutTimer?.Dispose();

            return await RunChildMediaLoopAsync(transport, capture, runToken, ct, heartbeatSeen, attemptGeneration);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            return AttemptOutcome.Canceled;
        }
        catch (OperationCanceledException)
        {
            return Volatile.Read(ref setupTimedOut[0])
                ? AttemptOutcome.SetupFailure(CreateReconnectTimeoutError())
                : AttemptOutcome.Stale;
        }
        catch (Exception exception)
        {
            return AttemptOutcome.SetupFailure(MapAdapterFailure(exception));
        }
        finally
        {
            UnregisterAttempt(attemptCts);
            await ReleaseCaptureAsync(capture);
            SetCaptureActive(false);
            if (transport is not null && heartbeatHandler is not null)
            {
                transport.HeartbeatReceived -= heartbeatHandler;
            }

            await ReleaseTransportAsync(transport);
            await ReleaseAdvertiserAsync(advertiser);
            await ReleaseListenerAsync(listener);
        }
    }

    private async Task<AttemptOutcome> RunParentAttemptAsync(CancellationToken runToken, bool initial)
    {
        using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(runToken);
        var ct = attemptCts.Token;
        RegisterAttempt(attemptCts);
        var attemptGeneration = Interlocked.Increment(ref _attemptGeneration);

        IPeerDiscoverySession? discovery = null;
        IRealtimeTransportSession? transport = null;
        IAudioPlaybackSession? playback = null;
        var heartbeatSeen = new bool[1];
        EventHandler? heartbeatHandler = null;
        var setupTimedOut = new bool[1];
        using CancellationTokenSource? setupTimeoutCts = initial
            ? null
            : CancellationTokenSource.CreateLinkedTokenSource(ct);
        using ITimer? setupTimeoutTimer = setupTimeoutCts is null
            ? null
            : _timeProvider.CreateTimer(
                _ =>
                {
                    Volatile.Write(ref setupTimedOut[0], true);
                    CancelQuietly(setupTimeoutCts);
                },
                null,
                _reconnectAttemptTimeout,
                Timeout.InfiniteTimeSpan);
        var setupToken = setupTimeoutCts?.Token ?? ct;

        try
        {
            var discoveryResult = await _discovery.StartAsync(setupToken);
            if (!discoveryResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(discoveryResult.Error!);
            }

            var discoverySession = discoveryResult.Value;
            discovery = discoverySession;
            PublishDiscovering(initial);
            SignalStarted();

            var discoveryFailure = discoverySession.LastError;
            EventHandler<PeerDiscoveryStateChangedEventArgs> discoveryHandler = (_, args) =>
            {
                if (args.State is PeerDiscoveryState.Failed)
                {
                    discoveryFailure = args.Error ?? discoverySession.LastError;
                }
            };

            DiscoveredPeer? peer = null;
            discoverySession.StateChanged += discoveryHandler;
            try
            {
                await foreach (var candidate in discoverySession.DiscoverPeersAsync(setupToken))
                {
                    peer = candidate;
                    break;
                }
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (Volatile.Read(ref setupTimedOut[0]))
            {
                return AttemptOutcome.SetupFailure(CreateReconnectTimeoutError());
            }
            catch (OperationCanceledException)
            {
                return AttemptOutcome.Stale;
            }
            finally
            {
                discoverySession.StateChanged -= discoveryHandler;
            }

            if (discoveryFailure is not null)
            {
                return AttemptOutcome.SetupFailure(discoveryFailure);
            }

            if (peer is null)
            {
                return AttemptOutcome.SetupFailure(new Error(
                    DiscoveryErrorCodes.ServiceNotFound,
                    "Детское устройство не найдено в локальной сети."));
            }

            // Discovery больше не нужен: освобождаем NSD-ресурсы до установки соединения.
            await ReleaseDiscoveryAsync(discovery);
            discovery = null;

            PublishParentConnecting();

            var connectResult = await _transport.ConnectAsync(peer.Endpoint, setupToken);
            if (!connectResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(connectResult.Error!);
            }

            transport = connectResult.Value;
            // Heartbeat-подписка сразу после создания transport-сессии и до playback:
            // первый pong не теряется в окне между handshake и запуском media loop (L7).
            _healthMonitor.Reset();
            heartbeatHandler = (_, _) => OnHeartbeat(heartbeatSeen);
            transport.HeartbeatReceived += heartbeatHandler;
            UpdateTransportState(transport.State);
            PublishConnected();

            var playbackResult = await _playback.StartAsync(new AudioPlaybackOptions(_codec.Format), ct);
            if (!playbackResult.IsSuccess)
            {
                return AttemptOutcome.SetupFailure(playbackResult.Error!);
            }

            playback = playbackResult.Value;
            SetPlaybackActive(true);
            setupTimeoutTimer?.Dispose();

            return await RunParentMediaLoopAsync(transport, playback, runToken, ct, heartbeatSeen, attemptGeneration);
        }
        catch (OperationCanceledException) when (runToken.IsCancellationRequested)
        {
            return AttemptOutcome.Canceled;
        }
        catch (OperationCanceledException)
        {
            return Volatile.Read(ref setupTimedOut[0])
                ? AttemptOutcome.SetupFailure(CreateReconnectTimeoutError())
                : AttemptOutcome.Stale;
        }
        catch (Exception exception)
        {
            return AttemptOutcome.SetupFailure(MapAdapterFailure(exception));
        }
        finally
        {
            UnregisterAttempt(attemptCts);
            await ReleasePlaybackAsync(playback);
            SetPlaybackActive(false);
            if (transport is not null && heartbeatHandler is not null)
            {
                transport.HeartbeatReceived -= heartbeatHandler;
            }

            await ReleaseTransportAsync(transport);
            await ReleaseDiscoveryAsync(discovery);
        }
    }

    private async Task<AttemptOutcome> RunChildMediaLoopAsync(
        IRealtimeTransportSession transport,
        IAudioCaptureSession capture,
        CancellationToken runToken,
        CancellationToken ct,
        bool[] heartbeatSeen,
        long attemptGeneration)
    {
        var failure = new TaskCompletionSource<Error>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediaFlowing = false;

        EventHandler<RealtimeTransportStateChangedEventArgs> transportHandler = (_, args) =>
        {
            UpdateTransportState(args.State);
            if (args.State is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                failure.TrySetResult(args.Error ?? transport.LastError ?? CreateMediaFlowLostError());
            }
        };

        transport.StateChanged += transportHandler;
        try
        {
            UpdateTransportState(transport.State);
            if (transport.State is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                return AttemptOutcome.SessionLost(
                    transport.LastError ?? CreateMediaFlowLostError(),
                    DisconnectReason.TransportError,
                    mediaFlowing: false);
            }

            using var mediaCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var mediaFlowingFlag = new bool[1];
            using var watchdog = StartMediaWatchdog(failure, mediaFlowingFlag, heartbeatSeen, attemptGeneration);
            var pcmFrameBytes = _codec.Format.Frame20MsBytes;
            var pcmBuffer = new byte[pcmFrameBytes];
            var encodedBuffer = new byte[_codec.MaxEncodedBytes];

            try
            {
                while (!mediaCts.IsCancellationRequested)
                {
                    Task<int> readTask;
                    try
                    {
                        readTask = capture.ReadAsync(pcmBuffer, mediaCts.Token).AsTask();
                    }
                    catch (Exception) when (mediaCts.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(mediaCts.Token);
                    }
                    catch (Exception exception) when (exception is not OperationCanceledException)
                    {
                        return AttemptOutcome.SessionLost(
                            MapCaptureFailure(exception),
                            DisconnectReason.MediaFlowLost,
                            mediaFlowing);
                    }

                    var completed = await Task.WhenAny(readTask, failure.Task);
                    if (completed == failure.Task)
                    {
                        var error = await failure.Task;
                        await CancelAndObserveAsync(mediaCts, readTask);
                        return AttemptOutcome.SessionLost(error, ReasonFromError(error), mediaFlowing);
                    }

                    int bytesRead;
                    try
                    {
                        bytesRead = await readTask;
                    }
                    catch (OperationCanceledException) when (mediaCts.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception) when (mediaCts.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(mediaCts.Token);
                    }
                    catch (Exception exception)
                    {
                        return AttemptOutcome.SessionLost(
                            MapCaptureFailure(exception),
                            DisconnectReason.MediaFlowLost,
                            mediaFlowing);
                    }

                    // Контракт платформенного адаптера: 0 байт означает конец потока захвата
                    // (0 возвращается только после StopAsync), а не «данных пока нет».
                    if (bytesRead == 0)
                    {
                        return AttemptOutcome.SessionLost(
                            new Error(AudioErrorCodes.CaptureUnavailable, "Захват микрофона завершился."),
                            DisconnectReason.MediaFlowLost,
                            mediaFlowing);
                    }

                    if (bytesRead < 0 || bytesRead > pcmBuffer.Length || bytesRead % pcmFrameBytes != 0)
                    {
                        return AttemptOutcome.SessionLost(
                            new Error(AudioErrorCodes.AdapterFailed, "Захват вернул PCM-блок некорректного размера."),
                            DisconnectReason.MediaFlowLost,
                            mediaFlowing);
                    }

                    for (var offset = 0; offset < bytesRead; offset += pcmFrameBytes)
                    {
                        mediaCts.Token.ThrowIfCancellationRequested();
                        int encodedBytes;
                        try
                        {
                            encodedBytes = _codec.Encode(pcmBuffer.AsSpan(offset, pcmFrameBytes), encodedBuffer);
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception)
                        {
                            return AttemptOutcome.SessionLost(
                                MapAudioFailure(exception),
                                DisconnectReason.MediaFlowLost,
                                mediaFlowing);
                        }

                        if (encodedBytes <= 0 || encodedBytes > encodedBuffer.Length)
                        {
                            return AttemptOutcome.SessionLost(
                                new Error(AudioErrorCodes.AdapterFailed, "Кодек вернул Opus-кадр некорректного размера."),
                                DisconnectReason.MediaFlowLost,
                                mediaFlowing);
                        }

                        try
                        {
                            await transport.SendAudioAsync(encodedBuffer.AsMemory(0, encodedBytes), mediaCts.Token);
                        }
                        catch (OperationCanceledException) when (mediaCts.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (RealtimeTransportClosedException)
                        {
                            return AttemptOutcome.SessionLost(
                                transport.LastError ?? CreateMediaFlowLostError(),
                                DisconnectReason.TransportError,
                                mediaFlowing);
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception)
                        {
                            return AttemptOutcome.SessionLost(
                                MapAdapterFailure(exception),
                                DisconnectReason.TransportError,
                                mediaFlowing);
                        }

                        RecordSentFrame();
                        _healthMonitor.MarkMediaFrame();
                        if (!mediaFlowing)
                        {
                            mediaFlowing = true;
                            Volatile.Write(ref mediaFlowingFlag[0], true);
                            PublishMediaFlowing();
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (runToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                return AttemptOutcome.SessionLost(
                    CreateNetworkChangedError(),
                    DisconnectReason.NetworkChanged,
                    mediaFlowing);
            }
            catch (Exception exception)
            {
                return AttemptOutcome.SessionLost(
                    MapAdapterFailure(exception),
                    DisconnectReason.TransportError,
                    mediaFlowing);
            }

            return AttemptOutcome.SessionLost(
                CreateNetworkChangedError(),
                DisconnectReason.NetworkChanged,
                mediaFlowing);
        }
        finally
        {
            transport.StateChanged -= transportHandler;
        }
    }

    private async Task<AttemptOutcome> RunParentMediaLoopAsync(
        IRealtimeTransportSession transport,
        IAudioPlaybackSession playback,
        CancellationToken runToken,
        CancellationToken ct,
        bool[] heartbeatSeen,
        long attemptGeneration)
    {
        var failure = new TaskCompletionSource<Error>(TaskCreationOptions.RunContinuationsAsynchronously);
        var mediaFlowing = false;

        EventHandler<RealtimeTransportStateChangedEventArgs> transportHandler = (_, args) =>
        {
            UpdateTransportState(args.State);
            if (args.State is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                failure.TrySetResult(args.Error ?? transport.LastError ?? CreateMediaFlowLostError());
            }
        };

        transport.StateChanged += transportHandler;
        try
        {
            UpdateTransportState(transport.State);
            if (transport.State is RealtimeTransportState.Failed or RealtimeTransportState.Closed)
            {
                return AttemptOutcome.SessionLost(
                    transport.LastError ?? CreateMediaFlowLostError(),
                    DisconnectReason.TransportError,
                    mediaFlowing: false);
            }

            using var mediaCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var mediaFlowingFlag = new bool[1];
            using var watchdog = StartMediaWatchdog(failure, mediaFlowingFlag, heartbeatSeen, attemptGeneration);
            var pcmBuffer = new byte[_codec.Format.Frame20MsBytes];
            var received = transport.ReceiveAudioAsync(mediaCts.Token).GetAsyncEnumerator(mediaCts.Token);
            try
            {
                try
                {
                    while (true)
                    {
                        mediaCts.Token.ThrowIfCancellationRequested();
                        Task<bool> moveTask;
                        try
                        {
                            moveTask = received.MoveNextAsync().AsTask();
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception) when (exception is not OperationCanceledException)
                        {
                            return AttemptOutcome.SessionLost(
                                MapAdapterFailure(exception),
                                DisconnectReason.TransportError,
                                mediaFlowing);
                        }

                        var completed = await Task.WhenAny(moveTask, failure.Task);
                        if (completed == failure.Task)
                        {
                            var error = await failure.Task;
                            await CancelAndObserveAsync(mediaCts, moveTask);
                            return AttemptOutcome.SessionLost(error, ReasonFromError(error), mediaFlowing);
                        }

                        bool hasNext;
                        try
                        {
                            hasNext = await moveTask;
                        }
                        catch (OperationCanceledException) when (mediaCts.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception)
                        {
                            return AttemptOutcome.SessionLost(
                                MapAdapterFailure(exception),
                                DisconnectReason.TransportError,
                                mediaFlowing);
                        }

                        if (!hasNext)
                        {
                            var error = transport.LastError ?? CreateMediaFlowLostError();
                            return AttemptOutcome.SessionLost(error, ReasonFromError(error), mediaFlowing);
                        }

                        // Принятый кадр — подтверждение жизни peer-а для ParentLossAlarm и telemetry.
                        RecordReceivedFrame();
                        if (_role is DeviceRole.Parent)
                        {
                            _parentLossAlarm.ReportPeerAlive();
                        }

                        var payload = received.Current.Payload;
                        mediaCts.Token.ThrowIfCancellationRequested();
                        int decodedBytes;
                        try
                        {
                            decodedBytes = _codec.Decode(payload.Span, pcmBuffer);
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception)
                        {
                            return AttemptOutcome.SessionLost(
                                MapAudioFailure(exception),
                                DisconnectReason.MediaFlowLost,
                                mediaFlowing);
                        }

                        if (decodedBytes <= 0 || decodedBytes > pcmBuffer.Length)
                        {
                            return AttemptOutcome.SessionLost(
                                new Error(AudioErrorCodes.AdapterFailed, "Кодек вернул PCM-блок некорректного размера."),
                                DisconnectReason.MediaFlowLost,
                                mediaFlowing);
                        }

                        try
                        {
                            await playback.WriteAsync(pcmBuffer.AsMemory(0, decodedBytes), mediaCts.Token);
                        }
                        catch (OperationCanceledException) when (mediaCts.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception) when (mediaCts.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(mediaCts.Token);
                        }
                        catch (Exception exception)
                        {
                            return AttemptOutcome.SessionLost(
                                MapPlaybackFailure(exception),
                                DisconnectReason.MediaFlowLost,
                                mediaFlowing);
                        }

                        _healthMonitor.MarkMediaFrame();
                        if (!mediaFlowing)
                        {
                            mediaFlowing = true;
                            Volatile.Write(ref mediaFlowingFlag[0], true);
                            PublishMediaFlowing();
                        }
                    }
                }
                catch (OperationCanceledException) when (runToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    return AttemptOutcome.SessionLost(
                        CreateNetworkChangedError(),
                        DisconnectReason.NetworkChanged,
                        mediaFlowing);
                }
                catch (Exception exception)
                {
                    return AttemptOutcome.SessionLost(
                        MapAdapterFailure(exception),
                        DisconnectReason.TransportError,
                        mediaFlowing);
                }
            }
            finally
            {
                await received.DisposeAsync();
            }
        }
        finally
        {
            transport.StateChanged -= transportHandler;
        }
    }

    private async Task FinalizeRunAsync(Error? unexpected)
    {
        // Терминальный Stopped не должен показывать тревогу; для Parent+Failed флаг форсируется
        // при построении снимка. Reset вне _sync: AlarmChanged-обработчик сам берёт _sync.
        _parentLossAlarm.Reset();
        CancellationTokenSource? runCts;
        MediaSessionSnapshot? terminalSnapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;

        lock (_sync)
        {
            runCts = _runCts;
            _runCts = null;
            _attemptCts = null;
            _transportState = null;
            _captureActive = false;
            _playbackActive = false;
            _mediaAlarmActive = false;
            _healthMonitor.Reset();

            if (_session is { } session && !session.IsTerminal)
            {
                if (_stopRequested)
                {
                    if (session.State is MonitoringState.Stopping)
                    {
                        session.MarkStopped();
                    }

                    terminalSnapshot = SetSnapshotLocked(MediaSessionState.Stopped, null);
                }
                else
                {
                    var error = unexpected ?? new Error(
                        MediaSessionErrorCodes.AdapterFailure,
                        "Media-сеанс завершился без явной причины.");
                    if (session.State is MonitoringState.Stopping)
                    {
                        session.MarkStopped();
                    }
                    else
                    {
                        session.MarkFailed(DisconnectReason.TransportError);
                    }

                    terminalSnapshot = SetSnapshotLocked(MediaSessionState.Failed, error);
                }
            }
            else if (_snapshot is { } current && !current.IsTerminal)
            {
                terminalSnapshot = SetSnapshotLocked(
                    _stopRequested ? MediaSessionState.Stopped : MediaSessionState.Failed,
                    _stopRequested ? null : unexpected ?? new Error(
                        MediaSessionErrorCodes.AdapterFailure,
                        "Media-сеанс завершился без явной причины."));
            }

            handler = SnapshotChanged;
        }

        // Разблокируем StartAsync, если запуск так и не дошёл до готовности.
        var startFailure = terminalSnapshot is { State: MediaSessionState.Failed } failed
            ? Result<MediaSessionSnapshot>.Failure(failed.Error!)
            : Result<MediaSessionSnapshot>.Failure(new Error(
                MediaSessionErrorCodes.InvalidState,
                "Запуск media-сеанса завершился до готовности."));
        _startTcs?.TrySetResult(startFailure);

        runCts?.Dispose();

        if (terminalSnapshot is not null)
        {
            handler?.Invoke(this, terminalSnapshot);
        }
    }

    private void OnNetworkChanged(object? sender, EventArgs e)
    {
        CancellationTokenSource? attempt;
        lock (_sync)
        {
            if (_disposed || _runTask is null || _runTask.IsCompleted)
            {
                return;
            }

            attempt = _attemptCts;
        }

        try
        {
            attempt?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Попытка уже завершилась и освободила CTS: отменять нечего.
        }
    }

    private void RegisterAttempt(CancellationTokenSource attemptCts)
    {
        lock (_sync)
        {
            _attemptCts = attemptCts;
        }
    }

    private void UnregisterAttempt(CancellationTokenSource attemptCts)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_attemptCts, attemptCts))
            {
                _attemptCts = null;
            }
        }
    }

    private async Task CancelRunAndWaitAsync()
    {
        Task? runTask;
        CancellationTokenSource? runCts;
        lock (_sync)
        {
            _stopRequested = true;
            runTask = _runTask;
            runCts = _runCts;
        }

        CancelQuietly(runCts);

        if (runTask is not null && !runTask.IsCompleted)
        {
            try
            {
                await runTask;
            }
            catch (OperationCanceledException)
            {
                // Отмена запуска — ожидаемое завершение.
            }
        }
    }

    private static void CancelQuietly(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Сеанс уже завершился и освободил CTS: отменять нечего.
        }
    }

    private static async Task CancelAndObserveAsync(CancellationTokenSource cts, Task pending)
    {
        await cts.CancelAsync();
        try
        {
            await pending;
        }
        catch (OperationCanceledException)
        {
            // Ожидание кадра отменено нами — исход сеанса уже определён ошибкой transport/media.
        }
    }

    private void OnHeartbeat(bool[] heartbeatSeen)
    {
        // Обработчик вызывается потоком транспорта: только короткие неблокирующие обновления.
        Volatile.Write(ref heartbeatSeen[0], true);
        _healthMonitor.MarkHeartbeat();
        _parentLossAlarm.ReportPeerAlive();
    }

    private void EvaluateParentAlarm()
    {
        if (Volatile.Read(ref _disposed) || Volatile.Read(ref _stopRequested))
        {
            return;
        }

        if (_role is DeviceRole.Parent)
        {
            _parentLossAlarm.Evaluate();
        }
    }

    private void OnParentAlarmChanged(object? sender, bool raised)
    {
        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed
                || _role is not DeviceRole.Parent
                || _stopRequested
                || _session is null
                || _snapshot is not { } current)
            {
                return;
            }

            // Alarm меняет только флаг в снимке: State/Error/Health остаются прежними, чтобы UI
            // показывал тревогу в текущем состоянии сеанса (MediaFlowing/Degraded/Reconnecting).
            var updated = CreateSnapshotLocked(current.State, current.Error);
            if (updated.ParentAlarmActive == current.ParentAlarmActive)
            {
                return;
            }

            _snapshot = updated;
            snapshot = updated;
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private void ActivateMediaAlarm(long attemptGeneration)
    {
        if (_role is not DeviceRole.Parent)
        {
            return;
        }

        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed
                || _stopRequested
                || Volatile.Read(ref _attemptGeneration) != attemptGeneration
                || _session is null
                || _snapshot is not { } current)
            {
                return;
            }

            // Media watchdog текущей попытки: тревога Parent поднимается по media loss независимо
            // от heartbeat (FR-002 007) и публикуется без смены State; state Degraded/Reconnecting
            // увидит флаг в следующих снимках.
            _mediaAlarmActive = true;
            var updated = CreateSnapshotLocked(current.State, current.Error);
            if (updated.ParentAlarmActive == current.ParentAlarmActive)
            {
                return;
            }

            _snapshot = updated;
            snapshot = updated;
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private void PublishLiveRefresh()
    {
        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed
                || _stopRequested
                || _session is null
                || _snapshot is not { } current
                || current.IsTerminal)
            {
                return;
            }

            // State/Error не меняются: обновляются только live-производные (telemetry, uptime,
            // health, ParentAlarmActive). Формирование снимка под _sync даёт консистентный набор.
            snapshot = CreateSnapshotLocked(current.State, current.Error);
            _snapshot = snapshot;
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private ITimer StartMediaWatchdog(
        TaskCompletionSource<Error> failure,
        bool[] mediaFlowing,
        bool[] heartbeatSeen,
        long attemptGeneration)
    {
        var startedAt = _timeProvider.GetUtcNow();
        return _timeProvider.CreateTimer(
            _ => OnMediaWatchdogTick(failure, mediaFlowing, heartbeatSeen, attemptGeneration, startedAt),
            null,
            MediaWatchdogPeriod,
            MediaWatchdogPeriod);
    }

    private void OnMediaWatchdogTick(
        TaskCompletionSource<Error> failure,
        bool[] mediaFlowing,
        bool[] heartbeatSeen,
        long attemptGeneration,
        DateTimeOffset startedAt)
    {
        if (Volatile.Read(ref _disposed))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var health = _healthMonitor.Evaluate(_role);

        // Control heartbeat проверяется независимо от media flow (FR-001/FR-002 007): сначала
        // «ни одного heartbeat с начала попытки», затем «heartbeat был, но control-канал потерян».
        // Typed heartbeat_missed отделён от media stall: это разные причины деградации.
        if (!Volatile.Read(ref heartbeatSeen[0]))
        {
            if (now - startedAt >= _healthMonitor.HeartbeatTimeout)
            {
                failure.TrySetResult(CreateHeartbeatMissedError());
            }
        }
        else if (health.Control is SessionChannelState.Lost)
        {
            failure.TrySetResult(CreateHeartbeatMissedError());
        }

        if (_role is DeviceRole.Parent)
        {
            // Локальная тревога Parent не зависит от Child: evaluator использует только TimeProvider.
            _parentLossAlarm.Evaluate();
        }

        if (!Volatile.Read(ref mediaFlowing[0]))
        {
            // Первый аудиокадр ещё не пришёл: если media path не подтвердился за порог, это
            // «signaling поднят, звука нет» — Degraded/Reconnecting (FR-018/FR-021). Проверка
            // отдельная от SessionHealthMonitor, потому что при полном отсутствии кадров media
            // оценивается как Inactive, а не Stalled.
            if (now - startedAt >= _healthMonitor.MediaTimeout
                && failure.TrySetResult(new Error(
                    RealtimeTransportErrorCodes.MediaFlowLost,
                    "Media path не подтвердился: первый аудиокадр не пришёл.")))
            {
                ActivateMediaAlarm(attemptGeneration);
            }

            return;
        }

        if (health.Media is AudioFlowState.Stalled
            && failure.TrySetResult(new Error(
                RealtimeTransportErrorCodes.MediaFlowLost,
                "Media flow остановился: аудиокадры не поступают.")))
        {
            ActivateMediaAlarm(attemptGeneration);
        }
    }

    private void PublishAdvertising(bool initial)
    {
        if (!initial)
        {
            return;
        }

        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed || _stopRequested)
            {
                return;
            }

            if (_session is { } session && session.State is MonitoringState.Idle)
            {
                session.BeginAdvertising();
            }

            if (_snapshot?.State is MediaSessionState.Connecting or MediaSessionState.Connected
                or MediaSessionState.MediaFlowing or MediaSessionState.Degraded
                or MediaSessionState.Reconnecting)
            {
                return;
            }

            snapshot = SetSnapshotLocked(MediaSessionState.Advertising, null);
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private void PublishDiscovering(bool initial)
    {
        if (!initial)
        {
            return;
        }

        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed || _stopRequested)
            {
                return;
            }

            if (_session is { } session && session.State is MonitoringState.Idle)
            {
                session.BeginDiscovery();
            }

            if (_snapshot?.State is MediaSessionState.Connecting or MediaSessionState.Connected
                or MediaSessionState.MediaFlowing or MediaSessionState.Degraded
                or MediaSessionState.Reconnecting)
            {
                return;
            }

            snapshot = SetSnapshotLocked(MediaSessionState.Discovering, null);
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private void PublishChildConnecting()
    {
        PublishTransition(MediaSessionState.Connecting, null, session =>
        {
            if (session.State is MonitoringState.Advertising)
            {
                session.EnterPairing();
            }

            if (session.State is MonitoringState.Pairing)
            {
                session.BeginConnecting();
            }
            else if (session.State is MonitoringState.Reconnecting)
            {
                session.BeginConnecting();
            }
        });
    }

    private void PublishParentConnecting()
    {
        PublishTransition(MediaSessionState.Connecting, null, session =>
        {
            if (session.State is MonitoringState.Discovering)
            {
                session.EnterPairing();
            }

            if (session.State is MonitoringState.Pairing)
            {
                session.BeginConnecting();
            }
            else if (session.State is MonitoringState.Reconnecting)
            {
                session.BeginConnecting();
            }
        });
    }

    private void PublishConnected()
    {
        PublishTransition(MediaSessionState.Connected, null, session =>
        {
            if (session.State is MonitoringState.Connecting)
            {
                session.MarkConnected();
            }
        });
    }

    private void PublishMediaFlowing()
    {
        if (_role is DeviceRole.Parent)
        {
            lock (_sync)
            {
                // Media loss alarm снимается восстановленным потоком до публикации MediaFlowing.
                _mediaAlarmActive = false;
            }

            // Reset + ReportPeerAlive: тревога снимается без окна ложного срабатывания —
            // сброшенная история last-alive сразу подтверждается жизнью peer-а (FR-002 007).
            _parentLossAlarm.Reset();
            _parentLossAlarm.ReportPeerAlive();
        }

        PublishTransition(MediaSessionState.MediaFlowing, null, session =>
        {
            if (session.State is MonitoringState.Connected or MonitoringState.Degraded)
            {
                session.MarkMediaFlowing();
            }
        });
    }

    private void PublishDegraded(DisconnectReason reason)
    {
        // MarkDegraded принимает только причины деградации; typed error (например peer_unavailable)
        // сохраняется в снимке Failed при исчерпании попыток, а КА фиксирует транспортную деградацию.
        var degradationReason = reason is DisconnectReason.HeartbeatTimeout or DisconnectReason.MediaFlowLost
            or DisconnectReason.TransportError or DisconnectReason.NetworkChanged or DisconnectReason.PeerClosed
            ? reason
            : DisconnectReason.TransportError;

        PublishTransition(MediaSessionState.Degraded, null, session =>
        {
            if (session.State is MonitoringState.Connected or MonitoringState.MediaFlowing)
            {
                session.MarkDegraded(degradationReason);
            }
        });
    }

    private void PublishReconnecting()
    {
        PublishTransition(MediaSessionState.Reconnecting, null, session =>
        {
            if (session.State is MonitoringState.Degraded)
            {
                session.BeginReconnecting();
            }
        });
    }

    private void FailStartup(Error error)
    {
        PublishTransition(MediaSessionState.Failed, error, session =>
        {
            if (!session.IsTerminal)
            {
                session.MarkFailed(DisconnectReason.ConnectFailed);
            }
        });

        _startTcs?.TrySetResult(Result<MediaSessionSnapshot>.Failure(error));
    }

    private void FailReconnect(Error error)
    {
        PublishTransition(MediaSessionState.Failed, error, session =>
        {
            if (session.IsTerminal)
            {
                return;
            }

            if (session.State is MonitoringState.Reconnecting)
            {
                session.MarkReconnectFailed(DisconnectReason.ReconnectExhausted);
            }
            else
            {
                session.MarkFailed(DisconnectReason.ReconnectExhausted);
            }
        });
    }

    private void SignalStarted()
    {
        var startTcs = _startTcs;
        if (startTcs is null)
        {
            return;
        }

        var snapshot = Snapshot;
        if (snapshot is not null)
        {
            startTcs.TrySetResult(Result<MediaSessionSnapshot>.Success(snapshot));
        }
    }

    private void PublishTransition(
        MediaSessionState state,
        Error? error,
        Action<MonitoringSession>? domainTransition)
    {
        MediaSessionSnapshot? snapshot = null;
        EventHandler<MediaSessionSnapshot>? handler = null;
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            if (_stopRequested && !IsStopPhaseState(state))
            {
                // Позднее обновление после начала ручной остановки игнорируется: сеанс уже Stopping.
                return;
            }

            if (_session is null)
            {
                return;
            }

            domainTransition?.Invoke(_session);
            snapshot = SetSnapshotLocked(state, error);
            handler = SnapshotChanged;
        }

        if (snapshot is not null)
        {
            handler?.Invoke(this, snapshot);
        }
    }

    private MediaSessionSnapshot? SetSnapshotLocked(MediaSessionState state, Error? error)
    {
        if (_session is null)
        {
            return null;
        }

        var snapshot = CreateSnapshotLocked(state, error);
        if (_snapshot is { } current
            && current.State == state
            && Equals(current.Error, error))
        {
            return null;
        }

        _snapshot = snapshot;
        return snapshot;
    }

    private MediaSessionSnapshot CreateSnapshotLocked(MediaSessionState state, Error? error)
    {
        var health = _session is null ? null : BuildHealthLocked();
        return new MediaSessionSnapshot(
            _role,
            state,
            error,
            _peerFingerprintShort,
            health,
            _session?.Id,
            _session is null ? null : BuildTelemetryLocked(),
            IsParentAlarmActiveLocked(state, error));
    }

    private bool IsParentAlarmActiveLocked(MediaSessionState state, Error? error) =>
        _role is DeviceRole.Parent
        && (_parentLossAlarm.IsRaised
            || _mediaAlarmActive
            || (state is MediaSessionState.Failed && error is not null && IsLossError(error)));

    private MediaSessionTelemetry BuildTelemetryLocked()
    {
        var lastMediaFrameAtMilliseconds = Interlocked.Read(ref _lastMediaFrameAtUnixMilliseconds);
        DateTimeOffset? lastMediaFrameAt = lastMediaFrameAtMilliseconds >= 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(lastMediaFrameAtMilliseconds)
            : null;
        var uptime = _timeProvider.GetUtcNow() - _telemetryStartedAt;
        return new MediaSessionTelemetry(
            Interlocked.Read(ref _sentFrames),
            Interlocked.Read(ref _receivedFrames),
            Volatile.Read(ref _reconnectAttempts),
            uptime < TimeSpan.Zero ? TimeSpan.Zero : uptime,
            lastMediaFrameAt);
    }

    private void RecordSentFrame()
    {
        Interlocked.Increment(ref _sentFrames);
        RecordMediaFrameTimestamp();
    }

    private void RecordReceivedFrame()
    {
        Interlocked.Increment(ref _receivedFrames);
        RecordMediaFrameTimestamp();
    }

    private void RecordMediaFrameTimestamp() =>
        Interlocked.Exchange(
            ref _lastMediaFrameAtUnixMilliseconds,
            _timeProvider.GetUtcNow().ToUnixTimeMilliseconds());

    private SessionHealth BuildHealthLocked()
    {
        var transportControl = _transportState switch
        {
            RealtimeTransportState.Connected => SessionChannelState.Connected,
            RealtimeTransportState.Failed or RealtimeTransportState.Closed => SessionChannelState.Lost,
            _ => SessionChannelState.Unknown
        };

        // Control-канал оценивается по heartbeat независимо от media flow: heartbeat Degraded/Lost
        // ухудшает health, даже если transport ещё формально Connected. Пока heartbeat не измерялся
        // (Unknown), сохраняется транспортная оценка, чтобы UI не показывал ложную потерю.
        var heartbeat = _healthMonitor.Evaluate(_role);
        var control = transportControl is SessionChannelState.Lost
            ? SessionChannelState.Lost
            : heartbeat.Control switch
            {
                SessionChannelState.Lost => SessionChannelState.Lost,
                SessionChannelState.Degraded => SessionChannelState.Degraded,
                _ => transportControl
            };

        return new SessionHealth(control, heartbeat.Media, _captureActive, _playbackActive);
    }

    private void UpdateTransportState(RealtimeTransportState state)
    {
        lock (_sync)
        {
            _transportState = state;
        }
    }

    private void SetCaptureActive(bool active)
    {
        lock (_sync)
        {
            _captureActive = active;
        }
    }

    private void SetPlaybackActive(bool active)
    {
        lock (_sync)
        {
            _playbackActive = active;
        }
    }

    private static bool IsStopPhaseState(MediaSessionState state) =>
        state is MediaSessionState.Stopping or MediaSessionState.Stopped or MediaSessionState.Failed;

    private static bool IsStoppableState(MonitoringState state) =>
        state is MonitoringState.Discovering or MonitoringState.Advertising or MonitoringState.Pairing
            or MonitoringState.Connecting or MonitoringState.Connected or MonitoringState.MediaFlowing
            or MonitoringState.Degraded or MonitoringState.Reconnecting;

    private static Error CreateNetworkChangedError() => new(
        RealtimeTransportErrorCodes.NetworkUnavailable,
        "Сеть изменилась; media-соединение пересоздаётся.");

    private static Error CreateMediaFlowLostError() => new(
        RealtimeTransportErrorCodes.MediaFlowLost,
        "Media flow потерян.");

    private static Error CreateHeartbeatMissedError() => new(
        RealtimeTransportErrorCodes.HeartbeatMissed,
        "Control heartbeat не поступает: связь с peer потеряна.");

    private static Error CreateReconnectTimeoutError() => new(
        RealtimeTransportErrorCodes.SignalingTimeout,
        "Повторное подключение не завершилось за отведённое время.");

    /// <summary>
    /// Коды, при которых terminal Failed Parent означает потерю peer-а (тревога уместна).
    /// Отказ доверия (unauthorized_peer/unsupported_version), локальные audio-сбои и adapter_failure
    /// не являются потерей связи и не должны включать loss-тревогу.
    /// </summary>
    private static bool IsLossError(Error error) =>
        error.Code is RealtimeTransportErrorCodes.PeerUnavailable
            or RealtimeTransportErrorCodes.SignalingTimeout
            or RealtimeTransportErrorCodes.HeartbeatMissed
            or RealtimeTransportErrorCodes.MediaFlowLost
            or RealtimeTransportErrorCodes.MediaPathAbsent
            or RealtimeTransportErrorCodes.NetworkUnavailable
            or RealtimeTransportErrorCodes.TlsFailure;

    private static bool IsDeviceAudioFailure(Error error) =>
        error.Code is AudioErrorCodes.CaptureUnavailable
            or AudioErrorCodes.PlaybackUnavailable
            or AudioErrorCodes.BufferUnavailable
            or AudioErrorCodes.AdapterFailed;

    private static Error MapAdapterFailure(Exception exception) => exception switch
    {
        TimeoutException => new Error(
            RealtimeTransportErrorCodes.SignalingTimeout,
            "Операция media-тракта не уложилась в отведённое время."),
        InvalidSessionTransitionException or InvalidOperationException or ArgumentException => new Error(
            MediaSessionErrorCodes.AdapterFailure,
            "Нарушен контракт media-адаптера."),
        _ => new Error(
            MediaSessionErrorCodes.AdapterFailure,
            "Media-адаптер завершился ошибкой.")
    };

    private static Error MapAudioFailure(Exception exception) => exception switch
    {
        TimeoutException => new Error(
            AudioErrorCodes.AdapterFailed,
            "Аудиоадаптер не уложился в отведённое время."),
        _ => new Error(
            AudioErrorCodes.AdapterFailed,
            "Аудиоадаптер завершился ошибкой.")
    };

    private static Error MapCaptureFailure(Exception exception) => exception switch
    {
        IOException => new Error(
            AudioErrorCodes.CaptureUnavailable,
            "Захват микрофона прерван ошибкой аудиоустройства."),
        ArgumentException => new Error(
            AudioErrorCodes.InvalidState,
            "Нарушен контракт захвата микрофона."),
        _ => new Error(
            AudioErrorCodes.AdapterFailed,
            "Захват микрофона завершился ошибкой.")
    };

    private static Error MapPlaybackFailure(Exception exception) => exception switch
    {
        IOException => new Error(
            AudioErrorCodes.PlaybackUnavailable,
            "Воспроизведение прервано ошибкой аудиоустройства."),
        ObjectDisposedException => new Error(
            AudioErrorCodes.InvalidState,
            "Воспроизведение остановлено до записи кадра."),
        ArgumentException => new Error(
            AudioErrorCodes.InvalidState,
            "Нарушен контракт воспроизведения."),
        _ => new Error(
            AudioErrorCodes.AdapterFailed,
            "Воспроизведение завершилось ошибкой.")
    };

    private static DisconnectReason ReasonFromError(Error error) => error.Code switch
    {
        RealtimeTransportErrorCodes.NetworkUnavailable => DisconnectReason.NetworkChanged,
        RealtimeTransportErrorCodes.MediaFlowLost => DisconnectReason.MediaFlowLost,
        RealtimeTransportErrorCodes.HeartbeatMissed => DisconnectReason.HeartbeatTimeout,
        RealtimeTransportErrorCodes.MediaPathAbsent => DisconnectReason.MediaFlowLost,
        RealtimeTransportErrorCodes.SessionClosed => DisconnectReason.PeerClosed,
        RealtimeTransportErrorCodes.PeerUnavailable => DisconnectReason.ConnectFailed,
        RealtimeTransportErrorCodes.SignalingTimeout => DisconnectReason.ConnectFailed,
        RealtimeTransportErrorCodes.TlsFailure => DisconnectReason.ConnectFailed,
        RealtimeTransportErrorCodes.UnauthorizedPeer => DisconnectReason.ConnectFailed,
        RealtimeTransportErrorCodes.UnsupportedVersion => DisconnectReason.ConnectFailed,
        RealtimeTransportErrorCodes.InvalidState => DisconnectReason.TransportError,
        AudioErrorCodes.CapturePermissionDenied => DisconnectReason.MediaFlowLost,
        AudioErrorCodes.CaptureUnavailable => DisconnectReason.MediaFlowLost,
        AudioErrorCodes.PlaybackUnavailable => DisconnectReason.MediaFlowLost,
        AudioErrorCodes.AdapterFailed => DisconnectReason.MediaFlowLost,
        AudioErrorCodes.BufferUnavailable => DisconnectReason.MediaFlowLost,
        AudioErrorCodes.InvalidState => DisconnectReason.MediaFlowLost,
        DiscoveryErrorCodes.WifiUnavailable => DisconnectReason.NetworkChanged,
        DiscoveryErrorCodes.ServiceNotFound => DisconnectReason.ConnectFailed,
        DiscoveryErrorCodes.ResolveFailed => DisconnectReason.ConnectFailed,
        DiscoveryErrorCodes.PermissionDenied => DisconnectReason.ConnectFailed,
        DiscoveryErrorCodes.NsdUnavailable => DisconnectReason.ConnectFailed,
        DiscoveryErrorCodes.InvalidTxt => DisconnectReason.ConnectFailed,
        _ => DisconnectReason.TransportError
    };

    private static async Task ReleaseCaptureAsync(IAudioCaptureSession? session)
    {
        if (session is null)
        {
            return;
        }

        await TryStopAsync(session.StopAsync);
        await ReleaseAsync(session);
    }

    private static async Task ReleasePlaybackAsync(IAudioPlaybackSession? session)
    {
        if (session is null)
        {
            return;
        }

        await TryStopAsync(session.StopAsync);
        await ReleaseAsync(session);
    }

    private static async Task ReleaseDiscoveryAsync(IPeerDiscoverySession? session)
    {
        if (session is null)
        {
            return;
        }

        await TryStopAsync(session.StopAsync);
        await ReleaseAsync(session);
    }

    private static async Task ReleaseAdvertiserAsync(IPeerAdvertiserSession? session)
    {
        if (session is null)
        {
            return;
        }

        await TryStopAsync(session.StopAsync);
        await ReleaseAsync(session);
    }

    private static async Task ReleaseTransportAsync(IRealtimeTransportSession? session)
    {
        if (session is null)
        {
            return;
        }

        try
        {
            await session.CloseAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // Закрытие best-effort: ниже DisposeAsync обязан освободить ресурсы сессии.
        }

        await ReleaseAsync(session);
    }

    private static async Task ReleaseListenerAsync(IRealtimeTransportListenerSession? session) =>
        await ReleaseAsync(session);

    private static async Task TryStopAsync(Func<CancellationToken, Task> stop)
    {
        try
        {
            await stop(CancellationToken.None);
        }
        catch (Exception)
        {
            // Stop — best-effort: DisposeAsync ниже обязан освободить ресурсы сеанса. Application
            // не логирует (нет ILogger); платформенная диагностика — ответственность адаптера.
        }
    }

    private static async Task ReleaseAsync(IAsyncDisposable? resource)
    {
        if (resource is null)
        {
            return;
        }

        try
        {
            await resource.DisposeAsync();
        }
        catch (Exception)
        {
            // Освобождение best-effort: сбой одного Dispose не должен блокировать освобождение
            // остальных ресурсов сеанса. Contract DisposeAsync адаптеров не предполагает исключений;
            // платформенная диагностика остаётся на стороне адаптера (Application не логирует).
        }
    }

    private enum AttemptKind
    {
        Canceled,
        Stale,
        SetupFailure,
        SessionLost
    }

    private readonly record struct AttemptOutcome(
        AttemptKind Kind,
        Error? Error,
        DisconnectReason Reason,
        bool MediaFlowing)
    {
        public static AttemptOutcome Canceled => new(AttemptKind.Canceled, null, DisconnectReason.ManualStop, false);

        public static AttemptOutcome Stale => new(AttemptKind.Stale, null, DisconnectReason.NetworkChanged, false);

        public static AttemptOutcome SetupFailure(Error error) =>
            new(AttemptKind.SetupFailure, error, DisconnectReason.ConnectFailed, false);

        public static AttemptOutcome SessionLost(Error error, DisconnectReason reason, bool mediaFlowing) =>
            new(AttemptKind.SessionLost, error, reason, mediaFlowing);
    }
}
