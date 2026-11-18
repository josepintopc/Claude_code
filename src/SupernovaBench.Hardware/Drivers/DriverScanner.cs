using System.Management;
using SupernovaBench.Core.Drivers;

namespace SupernovaBench.Hardware.Drivers;

/// <summary>Drivers instalados e dispositivos com problema (WMI).</summary>
public static class DriverScanner
{
    public static List<DriverInfo> Inventory()
    {
        var list = new List<DriverInfo>();
        foreach (var d in Query("SELECT DeviceName, DeviceClass, DriverProviderName, Manufacturer, DriverVersion, DriverDate, InfName, IsSigned, HardWareID, DeviceID FROM Win32_PnPSignedDriver"))
        {
            string name = S(d, "DeviceName");
            if (name.Length == 0) continue;
            list.Add(new DriverInfo
            {
                DeviceName = name,
                DeviceClass = S(d, "DeviceClass"),
                Provider = S(d, "DriverProviderName"),
                Manufacturer = S(d, "Manufacturer"),
                Version = S(d, "DriverVersion"),
                Date = DriverInfo.ParseWmiDate(d["DriverDate"]?.ToString()),
                InfName = S(d, "InfName"),
                IsSigned = d["IsSigned"] is bool b && b,
                HardwareId = S(d, "HardWareID"),
                DeviceId = S(d, "DeviceID"),
            });
        }
        return list.OrderBy(x => x.Category).ThenBy(x => x.DeviceName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static List<ProblemDevice> ProblemDevices()
    {
        var list = new List<ProblemDevice>();
        foreach (var d in Query("SELECT Name, PNPClass, ConfigManagerErrorCode, DeviceID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0"))
        {
            int code = Convert.ToInt32(d["ConfigManagerErrorCode"] ?? 0);
            if (code == 0) continue;
            string name = S(d, "Name");
            list.Add(new ProblemDevice(name.Length > 0 ? name : S(d, "DeviceID"), S(d, "PNPClass"), code, S(d, "DeviceID")));
        }
        // Problemas reais primeiro; "desconectado" (45) por último.
        return list.OrderBy(p => p.IsJustDisconnected).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Drivers de vídeo (placas físicas: NVIDIA, AMD ou Intel).</summary>
    public static List<DriverInfo> DisplayDrivers(IEnumerable<DriverInfo>? inventory = null)
        => (inventory ?? Inventory()).Where(d => d.Category == DriverCategory.Display && DriverInfo.GpuVendor(d.HardwareId) != null).ToList();

    private static List<ManagementObject> Query(string wql)
    {
        using var searcher = new ManagementObjectSearcher(@"root\cimv2", wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    private static string S(ManagementBaseObject mo, string prop)
    {
        try { return mo[prop]?.ToString()?.Trim() ?? ""; }
        catch (ManagementException) { return ""; }
    }
}
