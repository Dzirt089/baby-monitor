using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Domain.Tests.Devices;

/// <summary>
/// Контракт ролей устройства и их взаимного дополнения. Инвариант закреплён в AGENTS.md,
/// раздел 6: роли локального и удалённого устройства дополняют друг друга.
/// </summary>
public sealed class DeviceRoleExtensionsTests
{
    [Fact]
    public void Роль_ребёнка_дополняется_ролью_родителя()
    {
        DeviceRole complement = DeviceRole.Child.Complement();

        Assert.Equal(DeviceRole.Parent, complement);
    }

    [Fact]
    public void Роль_родителя_дополняется_ролью_ребёнка()
    {
        DeviceRole complement = DeviceRole.Parent.Complement();

        Assert.Equal(DeviceRole.Child, complement);
    }

    [Fact]
    public void Значение_роли_по_умолчанию_равно_Unknown()
    {
        // default(DeviceRole) не должен молча означать включение микрофона (роль Child):
        // sentinel Unknown — единственное безопасное значение по умолчанию.
        DeviceRole defaultRole = default;

        Assert.Equal(DeviceRole.Unknown, defaultRole);
        Assert.NotEqual(DeviceRole.Child, defaultRole);
    }

    [Fact]
    public void Комплементарная_роль_не_совпадает_с_исходной()
    {
        // Unknown — sentinel отсутствия выбора, а не допустимая роль: Complement для него бросает исключение.
        foreach (DeviceRole role in Enum.GetValues<DeviceRole>())
        {
            if (role is DeviceRole.Unknown)
            {
                continue;
            }

            Assert.NotEqual(role, role.Complement());
        }
    }

    [Fact]
    public void Дополнение_Unknown_бросает_ArgumentOutOfRangeException()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => DeviceRole.Unknown.Complement());

        Assert.Equal("role", exception.ParamName);
        Assert.Equal(DeviceRole.Unknown, exception.ActualValue);
    }

    [Fact]
    public void Дополнение_недопустимого_значения_бросает_ArgumentOutOfRangeException()
    {
        const DeviceRole invalidRole = (DeviceRole)999;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => invalidRole.Complement());

        Assert.Equal("role", exception.ParamName);
        Assert.Equal(invalidRole, exception.ActualValue);
    }
}
