using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Monitoring;

/// <summary>Проверка contract-набора на ручном test double foreground host.</summary>
public sealed class FakeForegroundSessionHostContractTests : ForegroundSessionContractTests
{
    protected override IForegroundSessionHost CreateHost() => new FakeForegroundSessionHost();

    [Fact]
    public async Task Fail_переводит_сессию_в_Failed_и_сохраняет_ошибку()
    {
        var host = new FakeForegroundSessionHost();
        var failure = new Error(ForegroundSessionErrorCodes.ServiceUnavailable, "Service недоступен.");
        await host.StartAsync(DeviceRole.Child, CancellationToken.None);

        host.Fail(failure);

        Assert.Equal(MonitoringForegroundState.Failed, host.Current!.State);
        Assert.Same(failure, host.Current.Error);
    }
}

/// <summary>Contract-подобные проверки INotificationPermission fake.</summary>
public sealed class FakeNotificationPermissionTests
{
    [Fact]
    public async Task Разрешённые_уведомления_возвращают_успех()
    {
        var permission = new FakeNotificationPermission();

        var result = await permission.EnsureGrantedAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task Отказ_возвращает_notification_permission_denied()
    {
        var permission = new FakeNotificationPermission(granted: false);

        var result = await permission.EnsureGrantedAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.NotificationPermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Отменённый_токен_бросает_OperationCanceledException()
    {
        var permission = new FakeNotificationPermission();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await permission.EnsureGrantedAsync(cts.Token));
    }
}

/// <summary>Проверки IMicrophonePermission fake (Child-only разрешение).</summary>
public sealed class FakeMicrophonePermissionTests
{
    [Fact]
    public async Task Разрешённый_микрофон_возвращает_успех()
    {
        var permission = new FakeMicrophonePermission();

        var result = await permission.EnsureGrantedAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value);
    }

    [Fact]
    public async Task Отказ_возвращает_microphone_permission_denied()
    {
        var permission = new FakeMicrophonePermission(granted: false);

        var result = await permission.EnsureGrantedAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(ForegroundSessionErrorCodes.MicrophonePermissionDenied, result.Error!.Code);
    }

    [Fact]
    public async Task Отменённый_токен_бросает_OperationCanceledException()
    {
        var permission = new FakeMicrophonePermission();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            async () => await permission.EnsureGrantedAsync(cts.Token));
    }
}
