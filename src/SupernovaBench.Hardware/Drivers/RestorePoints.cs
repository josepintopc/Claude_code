using System.Diagnostics;
using System.Management;
using SupernovaBench.Core.Drivers;

namespace SupernovaBench.Hardware.Drivers;

/// <summary>Pontos de restauração do sistema (WMI root\default:SystemRestore).</summary>
public static class RestorePoints
{
    private const int DeviceDriverInstall = 10, BeginSystemChange = 100;

    public static List<RestorePoint> List()
    {
        var list = new List<RestorePoint>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\default", "SELECT SequenceNumber, Description, CreationTime FROM SystemRestore");
            foreach (ManagementObject o in searcher.Get())
            {
                DateTime created = DateTime.MinValue;
                try { created = ManagementDateTimeConverter.ToDateTime(o["CreationTime"]?.ToString()); } catch (Exception) { }
                list.Add(new RestorePoint(Convert.ToInt32(o["SequenceNumber"] ?? 0), o["Description"]?.ToString() ?? "", created));
            }
        }
        catch (ManagementException) { }
        catch (UnauthorizedAccessException) { }
        return list.OrderByDescending(p => p.Created).ToList();
    }

    /// <summary>Cria um ponto "instalação de driver". O Windows recusa em silêncio se houver um nas últimas 24 h.</summary>
    public static RestorePointStatus Create(string description)
    {
        var before = List();
        try
        {
            using var cls = new ManagementClass(@"\\.\root\default", "SystemRestore", null);
            using var input = cls.GetMethodParameters("CreateRestorePoint");
            input["Description"] = description;
            input["RestorePointType"] = DeviceDriverInstall;
            input["EventType"] = BeginSystemChange;
            using var output = cls.InvokeMethod("CreateRestorePoint", input, null);
            uint code = Convert.ToUInt32(output?["ReturnValue"] ?? 1u);
            if (code == 1058 || code == 0x80070422) return RestorePointStatus.Disabled; // proteção/serviço desativado
            if (code != 0) return RestorePointStatus.Failed;
        }
        catch (ManagementException ex) when (ex.ErrorCode == ManagementStatus.NotFound || ex.ErrorCode == ManagementStatus.InvalidClass)
        {
            return RestorePointStatus.Disabled;
        }
        catch (Exception)
        {
            return RestorePointStatus.Failed;
        }
        var after = List();
        return after.Count > before.Count || (after.FirstOrDefault()?.Sequence ?? 0) > (before.FirstOrDefault()?.Sequence ?? 0)
            ? RestorePointStatus.Created
            : RestorePointStatus.SkippedFrequency;
    }

    public static void OpenSystemRestore() => Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });

    public static void OpenDeviceManager() => Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });
}

/// <summary>Cópia de segurança de todos os drivers de terceiros (pnputil /export-driver).</summary>
public static class DriverBackup
{
    /// <summary>Exporta para a pasta e devolve quantos drivers foram copiados. progress recebe cada linha do pnputil.</summary>
    public static async Task<int> ExportAsync(string folder, Action<string>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "pnputil.exe"), $"/export-driver * \"{folder}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("pnputil");
        using var reg = ct.Register(() => { try { p.Kill(); } catch (Exception) { } });
        string? line;
        while ((line = await p.StandardOutput.ReadLineAsync(ct)) != null)
            if (!string.IsNullOrWhiteSpace(line)) progress?.Invoke(line.Trim());
        await p.WaitForExitAsync(ct);
        // Cada driver exportado vira uma subpasta com o .inf.
        return Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).Count();
    }
}
