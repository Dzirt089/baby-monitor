namespace BabyMonitor.Domain.Devices;

/// <summary>Операции над ролями устройств.</summary>
public static class DeviceRoleExtensions
{
    /// <summary>
    /// Возвращает роль удалённой стороны для локальной роли: роли в сеансе
    /// взаимно дополняют друг друга, других комбинаций домен не допускает.
    /// </summary>
    /// <param name="role">Локальная роль устройства.</param>
    /// <returns>Дополняющая роль удалённого устройства.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Роль не входит в поддерживаемый набор значений.</exception>
    public static DeviceRole Complement(this DeviceRole role) => role switch
    {
        DeviceRole.Child => DeviceRole.Parent,
        DeviceRole.Parent => DeviceRole.Child,
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "Неизвестная роль устройства.")
    };
}
