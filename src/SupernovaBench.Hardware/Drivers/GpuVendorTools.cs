using System.Diagnostics;
using Microsoft.Win32;
using SupernovaBench.Core.Drivers;

namespace SupernovaBench.Hardware.Drivers;

/// <summary>Versão do driver de vídeo como cada fabricante mostra e links oficiais.</summary>
public static class GpuVendorTools
{
    public const string AmdDrivers = "https://www.amd.com/en/support/download/drivers.html";
    public const string IntelDsa = "https://www.intel.com/content/www/us/en/support/intel-driver-support-assistant.html";
    public const string NvidiaDrivers = "https://www.nvidia.com/Download/index.aspx";

    /// <summary>Versão "do fabricante": NVIDIA 566.36, AMD Adrenalin 24.12.1 (registro), Intel = versão do Windows.</summary>
    public static string? VendorVersion(DriverInfo d) => DriverInfo.GpuVendor(d.HardwareId) switch
    {
        "NVIDIA" => DriverVersions.NvidiaFromWindows(d.Version),
        "AMD" => Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\AMD\CN", "RadeonSoftwareVersion", null) as string ?? d.Version,
        _ => d.Version,
    };

    public static string? OfficialPage(string? vendor) => vendor switch
    {
        "NVIDIA" => NvidiaDrivers,
        "AMD" => AmdDrivers,
        "Intel" => IntelDsa,
        _ => null,
    };

    public static void Open(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
