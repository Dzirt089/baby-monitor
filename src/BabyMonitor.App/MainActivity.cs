namespace BabyMonitor.App;

/// <summary>
/// Единственная Activity приложения. Bootstrap-фаза: экран только сообщает, что
/// мониторинг ещё не реализован; разрешения, сервисы и аудио не запускаются.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetContentView(Resource.Layout.activity_main);
    }
}
