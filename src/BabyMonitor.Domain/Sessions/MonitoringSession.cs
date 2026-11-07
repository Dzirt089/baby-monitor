using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Domain.Sessions;

/// <summary>
/// Aggregate сеанса наблюдения: единственный КА состояний, инварианты ролей/терминальности,
/// отдельные control/media health и причины деградации. Не содержит Android/audio/transport деталей.
/// </summary>
public sealed class MonitoringSession
{
    private static readonly MonitoringState[] TerminalStates = [MonitoringState.Failed];

    private bool _stopped;

    private MonitoringSession(string id, DeviceRole localRole, TimeProvider timeProvider)
    {
        Id = id;
        LocalRole = localRole;
        RemoteRole = localRole is DeviceRole.Child ? DeviceRole.Parent : DeviceRole.Child;
        CreatedAt = timeProvider.GetUtcNow();
        State = MonitoringState.Idle;
        Health = SessionHealth.Initial;
    }

    /// <summary>Создаёт сеанс в состоянии Idle.</summary>
    /// <param name="id">Correlation identifier сеанса.</param>
    /// <param name="localRole">Роль локального устройства.</param>
    /// <param name="timeProvider">Инжектированное время.</param>
    /// <returns>Новый сеанс.</returns>
    /// <exception cref="ArgumentException">Пустой id или роль Unknown.</exception>
    public static MonitoringSession Create(string id, DeviceRole localRole, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (localRole is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Сеанс требует роль Child или Parent.", nameof(localRole));
        }

        return new MonitoringSession(id, localRole, timeProvider);
    }

    /// <summary>Correlation identifier сеанса.</summary>
    public string Id { get; }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole LocalRole { get; }

    /// <summary>Роль peer (всегда дополняет локальную).</summary>
    public DeviceRole RemoteRole { get; }

    /// <summary>Время создания сеанса.</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Текущее состояние КА.</summary>
    public MonitoringState State { get; private set; }

    /// <summary>Текущий health-снимок.</summary>
    public SessionHealth Health { get; private set; }

    /// <summary>Последняя причина деградации/завершения; null, пока причина не задана.</summary>
    public DisconnectReason? LastDisconnectReason { get; private set; }

    /// <summary>Сколько автоматических переподключений уже начато.</summary>
    public int ReconnectAttempts { get; private set; }

    /// <summary>Терминальное состояние: Failed или остановленный Idle.</summary>
    public bool IsTerminal => State is MonitoringState.Failed || (_stopped && State is MonitoringState.Idle);

    /// <summary>Child начинает публикацию сервиса.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход/роль.</exception>
    public void BeginAdvertising()
    {
        RequireRole(DeviceRole.Child, nameof(BeginAdvertising));
        TransitionFrom([MonitoringState.Idle], MonitoringState.Advertising, nameof(BeginAdvertising));
    }

    /// <summary>Parent начинает поиск Child.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход/роль.</exception>
    public void BeginDiscovery()
    {
        RequireRole(DeviceRole.Parent, nameof(BeginDiscovery));
        TransitionFrom([MonitoringState.Idle], MonitoringState.Discovering, nameof(BeginDiscovery));
    }

    /// <summary>Переходит к сопряжению.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void EnterPairing() =>
        TransitionFrom([MonitoringState.Discovering, MonitoringState.Advertising], MonitoringState.Pairing, nameof(EnterPairing));

    /// <summary>Начинает установку transport-соединения (в т.ч. после reconnect).</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void BeginConnecting() =>
        TransitionFrom([MonitoringState.Pairing, MonitoringState.Reconnecting], MonitoringState.Connecting, nameof(BeginConnecting));

    /// <summary>Transport-соединение установлено.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void MarkConnected()
    {
        TransitionFrom([MonitoringState.Connecting], MonitoringState.Connected, nameof(MarkConnected));
        Health = new SessionHealth(SessionChannelState.Connected, AudioFlowState.Inactive, false, false);
    }

    /// <summary>Media flow подтверждён; health отражает роль (Child capture, Parent playback).</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void MarkMediaFlowing()
    {
        TransitionFrom([MonitoringState.Connected, MonitoringState.Degraded], MonitoringState.MediaFlowing, nameof(MarkMediaFlowing));
        LastDisconnectReason = null;
        Health = new SessionHealth(
            SessionChannelState.Connected,
            AudioFlowState.Flowing,
            captureActive: LocalRole is DeviceRole.Child,
            playbackActive: LocalRole is DeviceRole.Parent);
    }

    /// <summary>Фиксирует деградацию: heartbeat или media flow, но сеанс ещё можно восстановить.</summary>
    /// <param name="reason">Причина: HeartbeatTimeout/MediaFlowLost/TransportError/NetworkChanged/PeerClosed.</param>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    /// <exception cref="ArgumentException">Причина None/ManualStop или не причина деградации.</exception>
    public void MarkDegraded(DisconnectReason reason)
    {
        if (reason is DisconnectReason.None or DisconnectReason.ManualStop)
        {
            throw new ArgumentException("Деградация требует конкретной причины.", nameof(reason));
        }

        TransitionFrom([MonitoringState.Connected, MonitoringState.MediaFlowing], MonitoringState.Degraded, nameof(MarkDegraded));
        LastDisconnectReason = reason;
        Health = new SessionHealth(
            SessionChannelState.Degraded,
            AudioFlowState.Stalled,
            captureActive: LocalRole is DeviceRole.Child,
            playbackActive: LocalRole is DeviceRole.Parent);
    }

    /// <summary>Начинает автоматическое переподключение после деградации.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void BeginReconnecting()
    {
        TransitionFrom([MonitoringState.Degraded], MonitoringState.Reconnecting, nameof(BeginReconnecting));
        ReconnectAttempts++;
    }

    /// <summary>Попытка переподключения не удалась; сеанс завершается ошибкой.</summary>
    /// <param name="reason">Причина, например ReconnectExhausted/ConnectFailed.</param>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void MarkReconnectFailed(DisconnectReason reason) =>
        MarkFailedFrom([MonitoringState.Reconnecting], reason, nameof(MarkReconnectFailed));

    /// <summary>Завершает сеанс ошибкой из любого не-терминального состояния.</summary>
    /// <param name="reason">Типизированная причина.</param>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void MarkFailed(DisconnectReason reason) =>
        MarkFailedFrom(
            [MonitoringState.Idle, MonitoringState.Discovering, MonitoringState.Advertising, MonitoringState.Pairing,
             MonitoringState.Connecting, MonitoringState.Connected, MonitoringState.MediaFlowing, MonitoringState.Degraded,
             MonitoringState.Reconnecting, MonitoringState.Stopping],
            reason,
            nameof(MarkFailed));

    /// <summary>Ручная остановка; отменяет reconnect loop.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void BeginStopping()
    {
        if (State is MonitoringState.Failed || (State is MonitoringState.Idle && _stopped))
        {
            throw new InvalidSessionTransitionException("Сеанс уже завершён и не может быть остановлен повторно.");
        }

        if (State is not (MonitoringState.Discovering or MonitoringState.Advertising or MonitoringState.Pairing
            or MonitoringState.Connecting or MonitoringState.Connected or MonitoringState.MediaFlowing
            or MonitoringState.Degraded or MonitoringState.Reconnecting))
        {
            throw new InvalidSessionTransitionException($"Недопустимый переход {State} → Stopping (BeginStopping).");
        }

        LastDisconnectReason = DisconnectReason.ManualStop;
        State = MonitoringState.Stopping;
    }

    /// <summary>Остановка завершена; сеанс становится терминальным Idle.</summary>
    /// <exception cref="InvalidSessionTransitionException">Недопустимый переход.</exception>
    public void MarkStopped()
    {
        TransitionFrom([MonitoringState.Stopping], MonitoringState.Idle, nameof(MarkStopped));
        _stopped = true;
        LastDisconnectReason = DisconnectReason.ManualStop;
        Health = SessionHealth.Initial;
    }

    /// <summary>Обновляет health-снимок (например, от SessionHealthMonitor).</summary>
    /// <param name="health">Новый снимок.</param>
    /// <exception cref="InvalidSessionTransitionException">Сеанс в терминальном/неподходящем состоянии.</exception>
    /// <exception cref="ArgumentException">Нарушение инвариантов ролей/согласованности.</exception>
    public void ApplyHealth(SessionHealth health)
    {
        ArgumentNullException.ThrowIfNull(health);
        if (State is not (MonitoringState.Connected or MonitoringState.MediaFlowing
            or MonitoringState.Degraded or MonitoringState.Reconnecting))
        {
            throw new InvalidSessionTransitionException($"Health нельзя применять в состоянии {State}.");
        }

        if (LocalRole is DeviceRole.Child && health.PlaybackActive)
        {
            throw new ArgumentException("Child не создаёт playback endpoint.", nameof(health));
        }

        if (LocalRole is DeviceRole.Parent && health.CaptureActive)
        {
            throw new ArgumentException("Parent не создаёт capture endpoint.", nameof(health));
        }

        Health = health;
    }

    private void MarkFailedFrom(MonitoringState[] allowed, DisconnectReason reason, string operation)
    {
        if (reason is DisconnectReason.None or DisconnectReason.ManualStop)
        {
            throw new ArgumentException("Завершение ошибкой требует конкретной причины.", nameof(reason));
        }

        if (IsTerminal)
        {
            throw new InvalidSessionTransitionException("Сеанс уже терминален.");
        }

        TransitionFrom(allowed, MonitoringState.Failed, operation);
        LastDisconnectReason = reason;
        Health = new SessionHealth(SessionChannelState.Lost, AudioFlowState.Inactive, false, false);
    }

    private void RequireRole(DeviceRole required, string operation)
    {
        if (LocalRole != required)
        {
            throw new InvalidSessionTransitionException(
                $"Операция {operation} недопустима для роли {LocalRole}: требуется {required}.");
        }
    }

    private void TransitionFrom(MonitoringState[] allowed, MonitoringState target, string operation)
    {
        if (IsTerminal)
        {
            throw new InvalidSessionTransitionException("Сеанс уже терминален.");
        }

        if (!TerminalStates.Contains(target) && !allowed.Contains(State))
        {
            throw new InvalidSessionTransitionException($"Недопустимый переход {State} → {target} ({operation}).");
        }

        if (TerminalStates.Contains(target) && !allowed.Contains(State))
        {
            throw new InvalidSessionTransitionException($"Недопустимый переход {State} → {target} ({operation}).");
        }

        State = target;
    }
}
