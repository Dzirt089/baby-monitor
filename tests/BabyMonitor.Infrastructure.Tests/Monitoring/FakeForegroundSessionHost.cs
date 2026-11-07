using BabyMonitor.Application.Abstractions.Monitoring;
using BabyMonitor.Application.Results;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.Infrastructure.Tests.Monitoring;

/// <summary>
/// Ручной test double IForegroundSessionHost/INotificationPermission для contract tests.
/// Не запускает Android service и не использует сеть/UI.
/// </summary>
internal sealed class FakeForegroundSessionHost : IForegroundSessionHost
{
    private ForegroundSessionSnapshot? _current;

    public ForegroundSessionSnapshot? Current => _current;

    public event EventHandler<ForegroundSessionSnapshot>? SnapshotChanged;

    public Task<Result<ForegroundSessionSnapshot>> StartAsync(DeviceRole role, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<ForegroundSessionSnapshot>>(cancellationToken);
        }

        if (role is not (DeviceRole.Child or DeviceRole.Parent))
        {
            return Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.InvalidRole, "Роль не поддерживает foreground-мониторинг.")));
        }

        if (_current is not null && !_current.IsTerminal)
        {
            return _current.Role == role
                ? Task.FromResult(Result<ForegroundSessionSnapshot>.Success(_current))
                : Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                    new Error(ForegroundSessionErrorCodes.AlreadyRunning, "Уже запущена foreground-сессия с другой ролью.")));
        }

        var snapshot = new ForegroundSessionSnapshot(role, MonitoringForegroundState.Running);
        Publish(snapshot);
        return Task.FromResult(Result<ForegroundSessionSnapshot>.Success(snapshot));
    }

    public Task<Result<ForegroundSessionSnapshot>> StopAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<ForegroundSessionSnapshot>>(cancellationToken);
        }

        if (_current is null || _current.IsTerminal)
        {
            return Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotRunning, "Нет активной foreground-сессии.")));
        }

        var stopped = new ForegroundSessionSnapshot(_current.Role, MonitoringForegroundState.Stopped);
        Publish(stopped);
        return Task.FromResult(Result<ForegroundSessionSnapshot>.Success(stopped));
    }

    public Task<Result<ForegroundSessionSnapshot>> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<ForegroundSessionSnapshot>>(cancellationToken);
        }

        return _current is null
            ? Task.FromResult(Result<ForegroundSessionSnapshot>.Failure(
                new Error(ForegroundSessionErrorCodes.NotRunning, "Нет активной foreground-сессии.")))
            : Task.FromResult(Result<ForegroundSessionSnapshot>.Success(_current));
    }

    /// <summary>Перевести сессию в Failed (для error-path contract tests).</summary>
    public void Fail(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var role = _current?.Role ?? DeviceRole.Child;
        Publish(new ForegroundSessionSnapshot(role, MonitoringForegroundState.Failed, error));
    }

    private void Publish(ForegroundSessionSnapshot snapshot)
    {
        _current = snapshot;
        SnapshotChanged?.Invoke(this, snapshot);
    }
}

/// <summary>Ручной test double INotificationPermission.</summary>
internal sealed class FakeNotificationPermission : INotificationPermission
{
    private readonly bool _granted;

    public FakeNotificationPermission(bool granted = true) => _granted = granted;

    public Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<bool>>(cancellationToken);
        }

        return _granted
            ? Task.FromResult(Result<bool>.Success(true))
            : Task.FromResult(Result<bool>.Failure(
                new Error(ForegroundSessionErrorCodes.NotificationPermissionDenied, "Нет разрешения на уведомления.")));
    }
}

/// <summary>Ручной test double IMicrophonePermission (Child-only разрешение).</summary>
internal sealed class FakeMicrophonePermission : IMicrophonePermission
{
    private readonly bool _granted;

    public FakeMicrophonePermission(bool granted = true) => _granted = granted;

    public Task<Result<bool>> EnsureGrantedAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<bool>>(cancellationToken);
        }

        return _granted
            ? Task.FromResult(Result<bool>.Success(true))
            : Task.FromResult(Result<bool>.Failure(new Error(
                ForegroundSessionErrorCodes.MicrophonePermissionDenied,
                "Нет разрешения на микрофон.")));
    }
}
