using Android.Content.PM;
using BabyMonitor.Platform.Android.ForegroundServices;
using BabyMonitor.Platform.Android.Permissions;

namespace BabyMonitor.App.Presentation;

/// <summary>
/// Базовая Activity: реализует видимость Activity и запрос runtime-permissions через
/// TaskCompletionSource. Не хранит бизнес-логику; только платформенный bridge для портов.
/// </summary>
public abstract class PermissionActivity : Activity, IActivityVisibility, IPermissionRequestHost
{
    private const int PermissionRequestCode = 7001;

    private readonly object _sync = new();
    private TaskCompletionSource<bool>? _pendingPermission;

    /// <inheritdoc />
    public bool IsVisible { get; private set; }

    /// <inheritdoc />
    public Task<bool> RequestPermissionsAsync(IReadOnlyList<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Count == 0)
        {
            return Task.FromResult(true);
        }

        return RequestPermissionsCoreAsync(permissions, cancellationToken);
    }

    /// <inheritdoc />
    protected override void OnResume()
    {
        base.OnResume();
        IsVisible = true;
    }

    /// <inheritdoc />
    protected override void OnPause()
    {
        IsVisible = false;
        base.OnPause();
    }

    /// <inheritdoc />
    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != PermissionRequestCode)
        {
            return;
        }

        TaskCompletionSource<bool>? pending;
        lock (_sync)
        {
            pending = _pendingPermission;
            _pendingPermission = null;
        }

        var granted = grantResults.Length > 0
            && grantResults.All(result => result == Permission.Granted);
        pending?.TrySetResult(granted);
    }

    private async Task<bool> RequestPermissionsCoreAsync(
        IReadOnlyList<string> permissions,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_pendingPermission is not null)
            {
                throw new InvalidOperationException("Одновременно может быть только один запрос разрешений.");
            }

            _pendingPermission = completion;
        }

        await using var registration = cancellationToken.Register(() =>
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pendingPermission, completion))
                {
                    _pendingPermission = null;
                }
            }

            completion.TrySetCanceled(cancellationToken);
        });

        RequestPermissions(permissions.ToArray(), PermissionRequestCode);
        return await completion.Task;
    }
}
