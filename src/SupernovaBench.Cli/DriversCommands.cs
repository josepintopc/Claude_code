using System.Runtime.Versioning;
using SupernovaBench.Core.Drivers;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware.Drivers;

namespace SupernovaBench.Cli;

/// <summary>"drivers list/check/restore-points" (só consulta; a CLI nunca instala drivers).</summary>
[SupportedOSPlatform("windows")]
internal static class DriversCommands
{
    public static int List(bool problemsOnly)
    {
        if (!problemsOnly)
        {
            var inventory = DriverScanner.Inventory();
            Console.WriteLine(Loc.T("drivers.count", inventory.Count));
            foreach (var g in inventory.GroupBy(d => d.Category))
            {
                Console.WriteLine($"{Loc.T(DriverInfo.CategoryKey(g.Key))} ({g.Count()})");
                foreach (var d in g)
                    Console.WriteLine($"  - {d.DeviceName} · {d.Provider} · {d.Version} · {d.Date?.ToString("d", Loc.Culture) ?? "—"}{(d.IsSigned ? "" : " · !")}");
            }
        }
        var problems = DriverScanner.ProblemDevices();
        Console.WriteLine($"{Loc.T("drivers.problems")}: {problems.Count}");
        foreach (var p in problems) Console.WriteLine($"  ! {p.Name}: {Loc.T(p.ErrorKey, p.ErrorCode)}");
        return 0;
    }

    public static async Task<int> Check()
    {
        foreach (var d in DriverScanner.DisplayDrivers())
        {
            string installed = GpuVendorTools.VendorVersion(d) ?? d.Version;
            if (DriverInfo.GpuVendor(d.HardwareId) != "NVIDIA")
            {
                Console.WriteLine($"{d.DeviceName}: {installed} · {GpuVendorTools.OfficialPage(DriverInfo.GpuVendor(d.HardwareId))}");
                continue;
            }
            try
            {
                var latest = await NvidiaUpdater.LatestAsync(d.DeviceName, studio: false, CancellationToken.None);
                Console.WriteLine(Loc.T("drivers.cli.nvidiaLatest", d.DeviceName, installed, latest?.Version ?? "—"));
                if (latest != null) Console.WriteLine($"  {latest.DownloadUrl}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{d.DeviceName}: {Loc.T("drivers.nvidia.error")} ({ex.Message})");
            }
        }

        Console.WriteLine(Loc.T("drivers.wu.searching"));
        try
        {
            var updates = await new WindowsUpdateDrivers().SearchAsync(CancellationToken.None);
            Console.WriteLine(updates.Count == 0 ? Loc.T("drivers.wu.none") : Loc.T("drivers.wu.found", updates.Count));
            foreach (var u in updates) Console.WriteLine($"  - {u.Title}");
        }
        catch (Exception ex)
        {
            Console.WriteLine(Loc.T("drivers.wu.error", ex.Message));
        }
        return 0;
    }

    public static int RestorePointsList()
    {
        var points = RestorePoints.List();
        Console.WriteLine($"{Loc.T("drivers.restore.title")}: {points.Count}");
        foreach (var p in points.Take(10)) Console.WriteLine($"  - {p.Created.ToString("g", Loc.Culture)} · {p.Description}");
        return 0;
    }
}
