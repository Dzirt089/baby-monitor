using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Application.Tests.Monitoring;

/// <summary>Инварианты immutable снимка foreground-сессии.</summary>
public sealed class ForegroundSessionSnapshotTests
{
    [Theory]
    [InlineData(DeviceRole.Child)]
    [InlineData(DeviceRole.Parent)]
    public void Снимок_принимает_роль_Child_или_Parent(DeviceRole role)
    {
        var snapshot = new ForegroundSessionSnapshot(role, MonitoringForegroundState.Running);

        Assert.Equal(role, snapshot.Role);
        Assert.Equal(MonitoringForegroundState.Running, snapshot.State);
        Assert.Null(snapshot.Error);
        Assert.False(snapshot.IsTerminal);
    }

    [Fact]
    public void Снимок_отклоняет_роль_Unknown()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new ForegroundSessionSnapshot(DeviceRole.Unknown, MonitoringForegroundState.Running));

        Assert.Equal("role", exception.ParamName);
    }

    [Fact]
    public void Failed_без_ошибки_запрещён()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Failed));
    }

    [Fact]
    public void Failed_с_ошибкой_терминален()
    {
        var snapshot = new ForegroundSessionSnapshot(
            DeviceRole.Child,
            MonitoringForegroundState.Failed,
            new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Service недоступен."));

        Assert.True(snapshot.IsTerminal);
        Assert.NotNull(snapshot.Error);
    }

    [Fact]
    public void Error_для_не_Failed_состояния_запрещён()
    {
        var error = new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Ошибка.");

        var exception = Assert.Throws<ArgumentException>(
            () => new ForegroundSessionSnapshot(DeviceRole.Child, MonitoringForegroundState.Running, error));

        Assert.Equal("error", exception.ParamName);
    }

    [Fact]
    public void Stopped_терминален()
    {
        var snapshot = new ForegroundSessionSnapshot(DeviceRole.Parent, MonitoringForegroundState.Stopped);

        Assert.True(snapshot.IsTerminal);
    }
}
