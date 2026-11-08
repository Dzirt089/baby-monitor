using System;
using Android.Runtime;
using Android.Util;
using BabyMonitor.App.Composition;
using BabyMonitor.Application.Media;

namespace BabyMonitor.App;

/// <summary>
/// Composition root приложения (Android Application): единственная точка, где создаются
/// долгоживущие зависимости и фабрика media-сеансов для foreground service. Activity и service
/// получают зависимости отсюда, а не собирают их сами (AGENTS.md §5); скрытых статических
/// синглтонов нет — composition root является экземпляром Application.
/// </summary>
[global::Android.App.Application]
public sealed class BabyMonitorApplication : global::Android.App.Application, IMonitoringMediaSessionFactoryProvider
{
    private const string LogTag = "BabyMonitor.Updates";

    private IMonitoringMediaSessionFactory? _mediaSessions;
    private UpdateComposition? _updates;

    /// <summary>Активационный конструктор .NET for Android.</summary>
    /// <param name="handle">JNI handle.</param>
    /// <param name="transfer">Правила владения JNI handle.</param>
    public BabyMonitorApplication(IntPtr handle, JniHandleOwnership transfer)
        : base(handle, transfer)
    {
    }

    /// <summary>Composition root обновлений (009); null до <see cref="OnCreate"/>.</summary>
    internal UpdateComposition? Updates => _updates;

    /// <summary>Фабрика media-сеансов приложения.</summary>
    /// <exception cref="InvalidOperationException">Application ещё не инициализирован.</exception>
    public IMonitoringMediaSessionFactory MediaSessions =>
        _mediaSessions ?? throw new InvalidOperationException("Application composition root не инициализирован.");

    /// <inheritdoc />
    public override void OnCreate()
    {
        base.OnCreate();
        _mediaSessions = new MediaSessionFactory(this);
        try
        {
            _updates = new UpdateComposition(this);
        }
        catch (Exception exception)
        {
            // Обновления — не критичная для мониторинга фича: при сбое composition экран их не откроет.
            Log.Warn(LogTag, "Update composition initialization failed: " + exception.GetType().Name);
        }
    }
}
