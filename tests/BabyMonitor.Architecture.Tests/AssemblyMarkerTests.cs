using BabyMonitor.Infrastructure;

namespace BabyMonitor.Architecture.Tests;

/// <summary>
/// Маркер сборки Infrastructure используется как однозначная точка привязки к сборке слоя.
/// Тест закрывает замечание ревью о том, что маркер нигде не используется.
/// </summary>
public sealed class AssemblyMarkerTests
{
    [Fact]
    public void Маркер_Infrastructure_принадлежит_сборке_слоя()
    {
        var assemblyName = typeof(InfrastructureAssemblyMarker).Assembly.GetName().Name;

        Assert.Equal("BabyMonitor.Infrastructure", assemblyName);
        Assert.Equal(assemblyName, InfrastructureAssemblyMarker.AssemblyName);
    }
}
