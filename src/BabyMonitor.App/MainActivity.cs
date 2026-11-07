using Android.App;
using Android.Content;
using Android.OS;
using Android.Widget;
using BabyMonitor.App.Presentation;
using BabyMonitor.Domain.Devices;

namespace BabyMonitor.App;

/// <summary>
/// Экран выбора роли. Роль выбирается явно и передаётся в PairingActivity (006 phase 2);
/// скрытого default нет, сопряжение предшествует мониторингу.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : PermissionActivity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(Resource.Layout.activity_main);

        FindViewById<Button>(Resource.Id.role_child)!.Click += (_, _) => OpenPairing(DeviceRole.Child);
        FindViewById<Button>(Resource.Id.role_parent)!.Click += (_, _) => OpenPairing(DeviceRole.Parent);
    }

    private void OpenPairing(DeviceRole role)
    {
        var intent = new Intent(this, typeof(PairingActivity));
        intent.PutExtra(PairingActivity.ExtraRole, role.ToString());
        StartActivity(intent);
    }
}
