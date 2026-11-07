using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Pairing;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Diagnostics;

/// <summary>
/// Immutable снимок диагностики: роль, состояние foreground-сессии и доверенные устройства.
/// Не содержит аудиоданных, ключей и персональных данных; коллекция оборонительно копируется.
/// </summary>
public sealed class DiagnosticsSnapshot
{
    private readonly IReadOnlyList<PairedDeviceSummary> _trustedDevices;

    /// <summary>Создаёт снимок диагностики.</summary>
    /// <param name="role">Роль локального устройства: Child или Parent.</param>
    /// <param name="foreground">Снимок foreground-сессии либо <see langword="null"/>, если сервис не запущен.</param>
    /// <param name="trustedDevices">Доверенные устройства; коллекция копируется.</param>
    /// <exception cref="ArgumentException">Роль не Child и не Parent.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="trustedDevices"/> равен <see langword="null"/>.</exception>
    public DiagnosticsSnapshot(
        DeviceRole role,
        ForegroundSessionSnapshot? foreground,
        IReadOnlyList<PairedDeviceSummary> trustedDevices)
    {
        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            throw new ArgumentException("Роль должна быть Child или Parent.", nameof(role));
        }

        ArgumentNullException.ThrowIfNull(trustedDevices);

        Role = role;
        Foreground = foreground;
        _trustedDevices = Array.AsReadOnly(trustedDevices.ToArray());
    }

    /// <summary>Роль локального устройства.</summary>
    public DeviceRole Role { get; }

    /// <summary>Снимок foreground-сессии; <see langword="null"/>, если сервис не запущен.</summary>
    public ForegroundSessionSnapshot? Foreground { get; }

    /// <summary>Доверенные устройства для локальной роли (read-only копия).</summary>
    public IReadOnlyList<PairedDeviceSummary> TrustedDevices => _trustedDevices;
}
