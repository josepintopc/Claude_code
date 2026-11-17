[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace SupernovaBench.Windows.Tests;

/// <summary>Teste que só faz sentido no Windows (Direct3D, d3dcompiler). Fora dele aparece como "pulado".</summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows()) Skip = "Requer Windows (Direct3D).";
    }
}
