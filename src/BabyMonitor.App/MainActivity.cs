using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using BabyMonitor.App.Presentation;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.App;

/// <summary>
/// Экран выбора роли. Роль выбирается явно и передаётся в MonitoringActivity; скрытого default нет.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : PermissionActivity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        FindViewById<Button>(Resource.Id.role_child)!.Click += (_, _) => OpenMonitoring(DeviceRole.Child);
        FindViewById<Button>(Resource.Id.role_parent)!.Click += (_, _) => OpenMonitoring(DeviceRole.Parent);
    }

    private void OpenMonitoring(DeviceRole role)
    {
        var intent = new Intent(this, typeof(MonitoringActivity));
        intent.PutExtra(MonitoringActivity.ExtraRole, role.ToString());
        StartActivity(intent);
    }
}
