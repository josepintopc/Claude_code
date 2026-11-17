using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Win32;

namespace SupernovaBench.Hardware;

public static class Elevation
{
    public static bool IsElevated
    {
        get
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }
}

/// <summary>
/// Driver PawnIO (necessário para temperatura/consumo da CPU via MSR/SMN no LibreHardwareMonitor).
/// É gratuito, assinado e distribuído pelo winget como "namazso.PawnIO".
/// </summary>
public static class PawnIO
{
    public const string WingetId = "namazso.PawnIO";
    public const string Website = "https://pawnio.eu";

    /// <summary>A biblioteca de sensores procura exatamente esta chave de desinstalação.</summary>
    public static bool IsInstalled
    {
        get
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
                using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO");
                return key != null;
            }
            catch { return false; }
        }
    }

    /// <summary>Instala pelo winget numa janela visível (o usuário acompanha). Retorna o código de saída.</summary>
    public static async Task<int> InstallWithWingetAsync(CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo("winget",
            $"install --id {WingetId} -e --accept-source-agreements --accept-package-agreements")
        {
            UseShellExecute = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("winget não iniciou");
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }
}
