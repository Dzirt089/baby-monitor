namespace BabyMonitor.Infrastructure;

/// <summary>
/// Маркер сборки Infrastructure. Позволяет диагностике и архитектурным тестам однозначно
/// идентифицировать адаптерный слой; реализации портов появятся вместе с feature spec.
/// </summary>
public static class InfrastructureAssemblyMarker
{
    /// <summary>Имя сборки слоя Infrastructure.</summary>
    public static string AssemblyName =>
        typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name ?? "BabyMonitor.Infrastructure";
}
