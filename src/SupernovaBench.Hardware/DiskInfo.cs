using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using SupernovaBench.Core.Info;
using static SupernovaBench.Core.Localization.Loc;

namespace SupernovaBench.Hardware;

/// <summary>Unidade (letra) que pode receber o benchmark de disco, com o disco físico por trás dela.</summary>
public sealed record DriveTarget(string Letter, string Root, string VolumeLabel, long FreeBytes, long TotalBytes, string FileSystem,
                                 int? DiskNumber, string Model, string Bus, string Media)
{
    /// <summary>Nome para o ranking: o modelo do disco ou, sem ele, a letra.</summary>
    public string DeviceName => string.IsNullOrWhiteSpace(Model) ? Letter : Model;
}

/// <summary>
/// Discos e saúde (SMART) no Windows. Junta três fontes, todas opcionais:
/// <list type="bullet">
/// <item>MSFT_PhysicalDisk + MSFT_StorageReliabilityCounter (qualquer disco: temperatura, desgaste, horas, erros);</item>
/// <item>MSStorageDriver_FailurePredict* em root\WMI (atributos SMART brutos dos discos SATA/ATA);</item>
/// <item>IOCTL_STORAGE_QUERY_PROPERTY com o log de saúde NVMe (página 0x02), como a Microsoft documenta.</item>
/// </list>
/// Precisa de administrador para a maior parte dos dados; o que faltar fica vazio.
/// </summary>
public static class DiskInfo
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    // ------------------------------------------------------------------ unidades

    public static IReadOnlyList<DriveTarget> Drives()
    {
        var letterToDisk = new Dictionary<char, int>();
        var disks = new Dictionary<int, (string Model, string Bus, string Media)>();
        try
        {
            foreach (var p in Query("SELECT DriveLetter, DiskNumber FROM MSFT_Partition", StorageScope))
            {
                char letter = p["DriveLetter"] is char c ? c : Convert.ToChar(p["DriveLetter"] ?? '\0');
                if (char.IsLetter(letter)) letterToDisk[char.ToUpperInvariant(letter)] = Convert.ToInt32(p["DiskNumber"] ?? -1);
            }
            foreach (var d in Query("SELECT DeviceId, FriendlyName, BusType, MediaType FROM MSFT_PhysicalDisk", StorageScope))
                if (int.TryParse(d["DeviceId"]?.ToString(), out int n))
                    disks[n] = (Str(d["FriendlyName"]), BusType(Convert.ToInt32(d["BusType"] ?? 0)), MediaType(Convert.ToInt32(d["MediaType"] ?? 0)));
        }
        catch
        {
            // Sem WMI de armazenamento (raro): as unidades aparecem só com a letra.
        }

        var list = new List<DriveTarget>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                string letter = drive.Name.TrimEnd('\\', '/');
                int? diskNumber = letter.Length > 0 && letterToDisk.TryGetValue(char.ToUpperInvariant(letter[0]), out int dn) ? dn : null;
                var (model, bus, media) = diskNumber is { } k && disks.TryGetValue(k, out var info) ? info : ("", "", "");
                list.Add(new DriveTarget(letter, drive.RootDirectory.FullName, drive.VolumeLabel, drive.AvailableFreeSpace, drive.TotalSize,
                                         drive.DriveFormat, diskNumber, model, bus, media));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return list;
    }

    // ------------------------------------------------------------------ saúde

    /// <summary>Saúde de todos os discos físicos, ordenados pelo número do disco.</summary>
    public static IReadOnlyList<DiskHealth> ReadHealth()
    {
        var result = new List<DiskHealth>();
        List<ManagementObject> physical;
        try { physical = Query("SELECT * FROM MSFT_PhysicalDisk", StorageScope); }
        catch { physical = new List<ManagementObject>(); }

        var ata = ReadAtaSmart();

        foreach (var pd in physical)
        {
            if (!int.TryParse(pd["DeviceId"]?.ToString(), out int number)) continue;
            string bus = BusType(Convert.ToInt32(pd["BusType"] ?? 0));
            var d = new DiskHealth
            {
                DiskNumber = number,
                Model = Str(pd["FriendlyName"]),
                Bus = bus,
                Media = MediaType(Convert.ToInt32(pd["MediaType"] ?? 0)),
                SizeBytes = Convert.ToInt64(pd["Size"] ?? 0L),
                Serial = Str(pd["SerialNumber"]),
                Firmware = Str(pd["FirmwareVersion"]),
            };

            ApplyReliability(d, pd);

            if (bus == "NVMe" && ReadNvmeLog(number) is { } log)
            {
                try { SmartParser.Apply(d, SmartParser.ParseNvme(log)); }
                catch (ArgumentException) { }
            }
            else if (ata.TryGetValue(number, out var a))
            {
                SmartParser.Apply(d, a.Attributes, a.PredictFailure);
            }

            if (!d.HasData && d.TemperatureC == null && d.WearPercent == null && d.PowerOnHours == null)
                d.UnavailableKey = Elevation.IsElevated ? "health.unavailable.driver" : "health.unavailable.admin";
            result.Add(d.Evaluate());
        }
        return result.OrderBy(d => d.DiskNumber).ToList();
    }

    /// <summary>MSFT_StorageReliabilityCounter do disco (pelo método GetStorageReliabilityCounter).</summary>
    private static void ApplyReliability(DiskHealth d, ManagementObject pd)
    {
        try
        {
            using var output = pd.InvokeMethod("GetStorageReliabilityCounter", null, null);
            if (output?["StorageReliabilityCounter"] is not ManagementBaseObject c) return;
            int? Small(string p) => c[p] is { } v && Convert.ToInt32(v) is int i && i > 0 ? i : null;
            long? Long(string p) => c[p] is { } v ? Convert.ToInt64(v) : null;

            d.TemperatureC = Small("Temperature");
            d.TemperatureMaxC = Small("TemperatureMax");
            // Em HDD o campo de desgaste vem 0 (não se aplica).
            if (c["Wear"] is { } wear && d.Media != "HDD") d.WearPercent = Convert.ToInt32(wear);
            d.PowerOnHours = Long("PowerOnHours") is > 0 and var h ? h : null;
            d.PowerCycles = Long("StartStopCycleCount") is > 0 and var s ? s : null;
            d.ReadErrors = Long("ReadErrorsUncorrected");
            d.WriteErrors = Long("WriteErrorsUncorrected");
            if (d.TemperatureC != null || d.PowerOnHours != null || d.WearPercent is > 0) d.Source = "wmi";
        }
        catch
        {
            // Driver sem suporte ou sem permissão.
        }
    }

    /// <summary>Atributos SMART brutos dos discos ATA/SATA, por número de disco.</summary>
    private static Dictionary<int, (List<SmartAttribute> Attributes, bool? PredictFailure)> ReadAtaSmart()
    {
        var result = new Dictionary<int, (List<SmartAttribute>, bool?)>();
        try
        {
            // InstanceName do root\WMI = PNPDeviceID do Win32_DiskDrive + "_0".
            var pnpToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var dd in Query("SELECT Index, PNPDeviceID FROM Win32_DiskDrive"))
                if (dd["PNPDeviceID"] is string pnp) pnpToIndex[pnp] = Convert.ToInt32(dd["Index"] ?? -1);

            int? IndexOf(ManagementBaseObject mo)
            {
                string name = mo["InstanceName"]?.ToString() ?? "";
                int cut = name.LastIndexOf('_');
                if (cut > 0 && pnpToIndex.TryGetValue(name[..cut], out int i)) return i;
                return pnpToIndex.TryGetValue(name, out i) ? i : null;
            }

            var thresholds = new Dictionary<int, byte[]>();
            foreach (var t in Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictThresholds", @"root\WMI"))
                if (IndexOf(t) is { } i && t["VendorSpecific"] is byte[] b) thresholds[i] = b;

            var predict = new Dictionary<int, bool>();
            foreach (var s in Query("SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus", @"root\WMI"))
                if (IndexOf(s) is { } i && s["PredictFailure"] is bool pf) predict[i] = pf;

            foreach (var d in Query("SELECT InstanceName, VendorSpecific FROM MSStorageDriver_FailurePredictData", @"root\WMI"))
            {
                if (IndexOf(d) is not { } i || d["VendorSpecific"] is not byte[] data) continue;
                var attrs = SmartParser.ParseAta(data, thresholds.GetValueOrDefault(i));
                if (attrs.Count > 0) result[i] = (attrs, predict.TryGetValue(i, out var p) ? p : null);
            }
        }
        catch
        {
            // root\WMI sem as classes de SMART (NVMe, USB, RAID, ou sem administrador).
        }
        return result;
    }

    // ------------------------------------------------------------------ NVMe (IOCTL)

    private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;
    private const int StorageDeviceProtocolSpecificProperty = 50, PropertyStandardQuery = 0;
    private const int ProtocolTypeNvme = 3, NVMeDataTypeLogPage = 2, NvmeLogHealth = 0x02, NvmeLogSize = 512;
    private const int ProtocolSpecificDataSize = 40;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[] inBuffer, int inSize, byte[] outBuffer, int outSize,
                                               out int returned, IntPtr overlapped);

    /// <summary>Lê o log de saúde NVMe (512 bytes) do disco físico N, ou null se o driver não suportar.</summary>
    public static byte[]? ReadNvmeLog(int diskNumber)
    {
        try
        {
            using var h = CreateFile($@"\\.\PhysicalDrive{diskNumber}", GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE,
                                     IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;

            // STORAGE_PROPERTY_QUERY (PropertyId, QueryType) + STORAGE_PROTOCOL_SPECIFIC_DATA + espaço do log.
            int size = 8 + ProtocolSpecificDataSize + NvmeLogSize;
            var buf = new byte[size];
            BitConverter.GetBytes(StorageDeviceProtocolSpecificProperty).CopyTo(buf, 0);
            BitConverter.GetBytes(PropertyStandardQuery).CopyTo(buf, 4);
            BitConverter.GetBytes(ProtocolTypeNvme).CopyTo(buf, 8);
            BitConverter.GetBytes(NVMeDataTypeLogPage).CopyTo(buf, 12);
            BitConverter.GetBytes(NvmeLogHealth).CopyTo(buf, 16);       // ProtocolDataRequestValue
            BitConverter.GetBytes(0).CopyTo(buf, 20);                   // ProtocolDataRequestSubValue
            BitConverter.GetBytes(ProtocolSpecificDataSize).CopyTo(buf, 24); // ProtocolDataOffset
            BitConverter.GetBytes(NvmeLogSize).CopyTo(buf, 28);         // ProtocolDataLength

            var output = new byte[size];
            if (!DeviceIoControl(h, IOCTL_STORAGE_QUERY_PROPERTY, buf, size, output, size, out int returned, IntPtr.Zero)) return null;

            // Saída: STORAGE_PROTOCOL_DATA_DESCRIPTOR = Version, Size, STORAGE_PROTOCOL_SPECIFIC_DATA (+ dados).
            int dataOffset = BitConverter.ToInt32(output, 8 + 16);
            int dataLength = BitConverter.ToInt32(output, 8 + 20);
            int start = 8 + dataOffset;
            if (dataLength < NvmeLogSize || start + NvmeLogSize > returned) return null;
            return output.AsSpan(start, NvmeLogSize).ToArray();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ utilitários

    private static List<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    private static string Str(object? v) => v?.ToString()?.Trim() ?? "";

    internal static string MediaType(int t) => t switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "" };

    internal static string BusType(int t) => t switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "File-backed", 16 => "Storage Spaces",
        17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => "",
    };

    /// <summary>Texto curto do veredito ("Bom (97%)"), no idioma atual.</summary>
    public static string StatusText(DiskHealth d) => d.Status switch
    {
        HealthStatus.Good => T("health.status.good"),
        HealthStatus.Caution => T("health.status.caution"),
        HealthStatus.Bad => T("health.status.bad"),
        _ => T("health.status.unknown"),
    } + (d.LifePercent is { } life ? $" ({life}%)" : "");
}
