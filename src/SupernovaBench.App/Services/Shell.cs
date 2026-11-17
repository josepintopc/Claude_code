using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SupernovaBench.App.Services;

/// <summary>Abre arquivos, pastas e links no programa padrão do Windows.</summary>
public static class Shell
{
    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { App.Log(ex); }
    }
}

/// <summary>Impede que o PC entre em suspensão enquanto um teste roda.</summary>
public static class PowerRequest
{
    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000,
    }

    [DllImport("kernel32.dll")]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState flags);

    /// <summary>Chame na thread da interface ao começar o teste.</summary>
    public static void KeepAwake() => SetThreadExecutionState(ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired);

    /// <summary>Chame na mesma thread ao terminar.</summary>
    public static void Release() => SetThreadExecutionState(ExecutionState.Continuous);
}
