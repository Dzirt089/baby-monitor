using BabyMonitor.Platform.Android.ForegroundServices;

namespace BabyMonitor.App;

/// <summary>
/// Единственная Activity приложения. Bootstrap-фаза: экран только сообщает, что
/// мониторинг ещё не реализован. Activity также сообщает видимость для запуска
/// foreground service только по явному действию пользователя.
/// </summary>
[Activity(Label = "@string/app_name", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity, IActivityVisibility
{
    /// <inheritdoc />
    public bool IsVisible { get; private set; }

    /// <inheritdoc />
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        SetContentView(Resource.Layout.activity_main);
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
}
