using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;
using SupernovaBench.Core.Games;

namespace SupernovaBench.Games;

/// <summary>Caminho do executável de cada processo aberto, sem precisar abrir o processo inteiro.</summary>
[SupportedOSPlatform("windows")]
public static class ProcessWatcher
{
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint PROCESS_SET_INFORMATION = 0x0200;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, char[] name, ref int size);

    public static string? ImagePath(int pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[1024];
            int size = buf.Length;
            return QueryFullProcessImageName(h, 0, buf, ref size) ? new string(buf, 0, size) : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    /// <summary>(pid, executável) de todos os processos que dá para ler.</summary>
    public static List<(int Pid, string Path)> Snapshot()
    {
        var list = new List<(int, string)>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (p.Id <= 4) continue;
                if (ImagePath(p.Id) is { } path) list.Add((p.Id, path));
            }
        }
        return list;
    }

    /// <summary>Muda a prioridade dos processos desse executável (jogos com anti-cheat podem recusar).</summary>
    public static int TrySetPriority(string exePath, PriorityChoice priority)
    {
        var cls = priority switch
        {
            PriorityChoice.High => ProcessPriorityClass.High,
            PriorityChoice.AboveNormal => ProcessPriorityClass.AboveNormal,
            _ => ProcessPriorityClass.Normal,
        };
        int changed = 0;
        foreach (var (pid, path) in Snapshot())
        {
            if (!string.Equals(path, exePath, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                using var p = Process.GetProcessById(pid);
                p.PriorityClass = cls;
                changed++;
            }
            catch (Exception) { }
        }
        return changed;
    }
}

/// <summary>Planos de energia do Windows (powrprof.dll).</summary>
[SupportedOSPlatform("windows")]
public static class PowerPlans
{
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid Ultimate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    [DllImport("powrprof.dll")]
    private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);

    [DllImport("powrprof.dll")]
    private static extern uint PowerEnumerate(IntPtr root, IntPtr schemeGuid, IntPtr subGroup, uint accessFlags, uint index, byte[]? buffer, ref uint size);

    [DllImport("powrprof.dll", CharSet = CharSet.Unicode)]
    private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subGroup, IntPtr setting, byte[]? buffer, ref uint size);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr mem);

    private const uint ACCESS_SCHEME = 16;

    public static Guid? Active()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    public static bool SetActive(Guid scheme) => PowerSetActiveScheme(IntPtr.Zero, ref scheme) == 0;

    /// <summary>Planos existentes neste PC (GUID e nome).</summary>
    public static List<(Guid Id, string Name)> List()
    {
        var list = new List<(Guid, string)>();
        for (uint i = 0; ; i++)
        {
            var buf = new byte[16];
            uint size = 16;
            if (PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, ACCESS_SCHEME, i, buf, ref size) != 0) break;
            var id = new Guid(buf);
            list.Add((id, FriendlyName(id)));
        }
        return list;
    }

    private static string FriendlyName(Guid id)
    {
        uint size = 0;
        PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, null, ref size);
        if (size == 0) return id.ToString();
        var buf = new byte[size];
        if (PowerReadFriendlyName(IntPtr.Zero, ref id, IntPtr.Zero, IntPtr.Zero, buf, ref size) != 0) return id.ToString();
        return System.Text.Encoding.Unicode.GetString(buf).TrimEnd('\0');
    }

    /// <summary>GUID do plano escolhido no perfil, se existir neste PC (desempenho máximo cai para alto desempenho).</summary>
    public static Guid? Resolve(PowerPlanChoice choice, IReadOnlyCollection<Guid> available) => choice switch
    {
        PowerPlanChoice.Ultimate when available.Contains(Ultimate) => Ultimate,
        PowerPlanChoice.Ultimate or PowerPlanChoice.HighPerformance when available.Contains(HighPerformance) => HighPerformance,
        PowerPlanChoice.Balanced when available.Contains(Balanced) => Balanced,
        _ => null,
    };
}

/// <summary>GPU preferida por executável: o mesmo registro das Configurações de gráficos do Windows.</summary>
[SupportedOSPlatform("windows")]
public static class GpuPreference
{
    private const string KeyPath = @"Software\Microsoft\DirectX\UserGpuPreferences";

    public static GpuChoice Get(string exe)
    {
        using var k = Registry.CurrentUser.OpenSubKey(KeyPath);
        return Parse(k?.GetValue(exe) as string);
    }

    public static void Set(string exe, GpuChoice choice)
    {
        using var k = Registry.CurrentUser.CreateSubKey(KeyPath);
        if (choice == GpuChoice.Default) k.DeleteValue(exe, throwOnMissingValue: false);
        else k.SetValue(exe, $"GpuPreference={(choice == GpuChoice.HighPerformance ? 2 : 1)};");
    }

    public static GpuChoice Parse(string? value)
    {
        if (value == null) return GpuChoice.Default;
        foreach (var part in value.Split(';'))
        {
            var kv = part.Split('=');
            if (kv.Length == 2 && kv[0].Trim() == "GpuPreference")
                return kv[1].Trim() switch { "2" => GpuChoice.HighPerformance, "1" => GpuChoice.PowerSaving, _ => GpuChoice.Default };
        }
        return GpuChoice.Default;
    }
}

/// <summary>Abre jogos e pastas.</summary>
[SupportedOSPlatform("windows")]
public static class GameLauncher
{
    public static void Launch(GameEntry game)
    {
        if (!string.IsNullOrEmpty(game.LaunchUri))
        {
            // shell:AppsFolder precisa passar pelo Explorer.
            if (game.LaunchUri.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo("explorer.exe", game.LaunchUri) { UseShellExecute = true });
            else
                Process.Start(new ProcessStartInfo(game.LaunchUri) { UseShellExecute = true });
            return;
        }
        if (!string.IsNullOrEmpty(game.LaunchExecutable) && File.Exists(game.LaunchExecutable))
        {
            Process.Start(new ProcessStartInfo(game.LaunchExecutable, game.LaunchArguments ?? "") { UseShellExecute = true });
            return;
        }
        if (!string.IsNullOrEmpty(game.Executable) && File.Exists(game.Executable))
        {
            Process.Start(new ProcessStartInfo(game.Executable, game.Arguments ?? "")
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(game.Executable) ?? game.InstallDir,
            });
            return;
        }
        throw new FileNotFoundException(game.Executable ?? game.InstallDir);
    }

    public static void OpenFolder(GameEntry game)
    {
        if (Directory.Exists(game.InstallDir))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{game.InstallDir}\"") { UseShellExecute = true });
    }
}
