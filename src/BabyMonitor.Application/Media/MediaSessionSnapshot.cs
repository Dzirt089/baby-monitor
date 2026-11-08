using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;
using BabyMonitor.Domain.Sessions;

namespace BabyMonitor.Application.Media;

/// <summary>
/// Immutable снимок media-сеанса для presentation-слоя: роль, состояние, типизированная ошибка,
/// короткая форма fingerprint peer, health, correlation id, telemetry-агрегаты и локальная
/// тревога Parent. Не содержит аудиоданных, полного fingerprint, секретов и платформенных типов;
/// коллекций нет, поэтому defensive copy не требуется (все вложенные значения immutable).
/// </summary>
public sealed record MediaSessionSnapshot
{
    private const int ShortFingerprintLength = 8;

    /// <summary>Создаёт снимок media-сеанса.</summary>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <param name="state">Состояние media-сеанса.</param>
    /// <param name="error">Типизированная ошибка; обязательна для <see cref="MediaSessionState.Failed"/> и запрещена для остальных состояний.</param>
    /// <param name="peerFingerprintShort">Первые 8 hex-символов fingerprint peer либо null, если identity неизвестна.</param>
    /// <param name="health">Health-снимок control/media/capture/playback либо null до запуска.</param>
    /// <param name="sessionId">Correlation identifier сеанса либо null до первого запуска.</param>
    /// <param name="telemetry">Telemetry-агрегаты сеанса либо null, если телеметрия недоступна.</param>
    /// <param name="parentAlarmActive">
    /// Локальная тревога Parent активна; true допустим только для роли Parent.
    /// </param>
    /// <exception cref="ArgumentException">
    /// Некорректная роль, ошибка не по состоянию, неверный формат fingerprint или тревога Parent у Child.
    /// </exception>
    /// <exception cref="ArgumentNullException">Для состояния Failed не передана ошибка.</exception>
    public MediaSessionSnapshot(
        DeviceRole role,
        MediaSessionState state,
        Error? error = null,
        string? peerFingerprintShort = null,
        SessionHealth? health = null,
        string? sessionId = null,
        MediaSessionTelemetry? telemetry = null,
        bool parentAlarmActive = false)
    {
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Media-сеанс требует роль Child или Parent.", nameof(role));
        }

        if (state is MediaSessionState.Failed)
        {
            ArgumentNullException.ThrowIfNull(error, nameof(error));
        }
        else if (error is not null)
        {
            throw new ArgumentException("Error допустим только для состояния Failed.", nameof(error));
        }

        if (peerFingerprintShort is not null)
        {
            if (peerFingerprintShort.Length != ShortFingerprintLength
                || !peerFingerprintShort.All(char.IsAsciiHexDigitLower))
            {
                throw new ArgumentException(
                    "Короткий fingerprint peer должен состоять из 8 hex-символов в нижнем регистре.",
                    nameof(peerFingerprintShort));
            }
        }

        if (sessionId is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        }

        if (parentAlarmActive && role is not DeviceRole.Parent)
        {
            throw new ArgumentException(
                "ParentAlarmActive допустим только для роли Parent.",
                nameof(parentAlarmActive));
        }

        Role = role;
        State = state;
        Error = error;
        PeerFingerprintShort = peerFingerprintShort;
        Health = health;
        SessionId = sessionId;
        Telemetry = telemetry;
        ParentAlarmActive = parentAlarmActive;
    }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole Role { get; }

    /// <summary>Состояние media-сеанса.</summary>
    public MediaSessionState State { get; }

    /// <summary>Типизированная ошибка для состояния Failed; null в остальных случаях.</summary>
    public Error? Error { get; }

    /// <summary>Первые 8 hex-символов fingerprint peer для отображения; полный fingerprint не раскрывается.</summary>
    public string? PeerFingerprintShort { get; }

    /// <summary>Health-снимок (control/media/capture/playback); null до запуска сеанса.</summary>
    public SessionHealth? Health { get; }

    /// <summary>Correlation identifier сеанса; null до первого запуска.</summary>
    public string? SessionId { get; }

    /// <summary>Telemetry-агрегаты сеанса; null, если снимок создан без телеметрии.</summary>
    public MediaSessionTelemetry? Telemetry { get; }

    /// <summary>
    /// Локальная тревога Parent активна: peer не отвечает дольше порога или сеанс завершился
    /// ошибкой. Для роли Child всегда false.
    /// </summary>
    public bool ParentAlarmActive { get; }

    /// <summary>Сеанс активен (запуск, работа или остановка).</summary>
    public bool IsRunning => State is MediaSessionState.Starting or MediaSessionState.Advertising
        or MediaSessionState.Discovering or MediaSessionState.Connecting or MediaSessionState.Connected
        or MediaSessionState.MediaFlowing or MediaSessionState.Degraded or MediaSessionState.Reconnecting
        or MediaSessionState.Stopping;

    /// <summary>Терминальное состояние (Stopped или Failed).</summary>
    public bool IsTerminal => State is MediaSessionState.Stopped or MediaSessionState.Failed;
}
