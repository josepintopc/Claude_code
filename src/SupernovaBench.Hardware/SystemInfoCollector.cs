using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using SupernovaBench.Core;
using SupernovaBench.Core.Info;
using static SupernovaBench.Core.Localization.Loc;

namespace SupernovaBench.Hardware;

/// <summary>Resumo rápido para a Visão geral.</summary>
public sealed record PcSummary(string Cpu, string Gpu, string Ram, string Board, string Os, string Storage);

/// <summary>
/// Coleta informações estáticas do sistema via WMI e registro (estilo CPU-Z / AIDA64).
/// Os rótulos saem no idioma atual; ao trocar de idioma basta coletar de novo.
/// <para>Baseado no SystemInfoCollector do ResetExplode (MIT, © 2026 Gustavo / GustavoReset).</para>
/// </summary>
public static class SystemInfoCollector
{
    public static List<InfoSection> CollectAll() => new()
    {
        Section("cpu", T("hw.tab.cpu"), CollectCpu),
        Section("board", T("hw.tab.board"), CollectBoard),
        Section("memory", T("hw.tab.memory"), CollectRam),
        Section("gpu", T("hw.tab.gpu"), CollectGpu),
        Section("storage", T("hw.tab.storage"), CollectDisks),
        Section("os", T("hw.tab.os"), CollectOs),
    };

    public static PcSummary CollectSummary()
    {
        string cpu = MachineInfo.CpuName();
        string gpu = Try(() => string.Join(" + ", Query("SELECT Name FROM Win32_VideoController").Select(g => S(g, "Name"))
                                                       .Where(n => n != "—").Distinct()));
        string ram = Try(() =>
        {
            ulong total = 0;
            string type = "";
            int speed = 0;
            foreach (var m in Query("SELECT Capacity, SMBIOSMemoryType, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
            {
                total += Convert.ToUInt64(m["Capacity"] ?? 0UL);
                type = RamType(m["SMBIOSMemoryType"]);
                speed = Math.Max(speed, Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0));
            }
            return $"{Bytes(total)} {type}{(speed > 0 ? $" @ {speed} MT/s" : "")}".Trim();
        });
        string board = Try(() => Query("SELECT Manufacturer, Product FROM Win32_BaseBoard")
                                     .Select(b => $"{S(b, "Manufacturer")} {S(b, "Product")}").FirstOrDefault() ?? "—");
        string os = Try(() => Query("SELECT Caption, BuildNumber FROM Win32_OperatingSystem")
                                  .Select(o => $"{S(o, "Caption").Replace("Microsoft ", "")} (build {S(o, "BuildNumber")})")
                                  .FirstOrDefault() ?? "—");
        string storage = Try(() =>
        {
            var disks = Query("SELECT Size FROM Win32_DiskDrive").Select(d => Convert.ToUInt64(d["Size"] ?? 0UL)).Where(s => s > 0).ToList();
            return disks.Count == 0 ? "—" : $"{disks.Count} × ({string.Join(" + ", disks.Select(d => Bytes(d)))})";
        });
        return new PcSummary(cpu, gpu, ram, board, os, storage);
    }

    // ------------------------------------------------------------------ utilidades

    private static InfoSection Section(string id, string title, Action<List<InfoNode>> fill)
    {
        var s = new InfoSection { Id = id, Title = title };
        try { fill(s.Nodes); }
        catch (Exception ex)
        {
            var err = new InfoNode { Label = T("hw.error") };
            err.Add(ex.GetType().Name, ex.Message);
            s.Nodes.Add(err);
        }
        return s;
    }

    private static string Try(Func<string> f)
    {
        try
        {
            var v = f();
            return string.IsNullOrWhiteSpace(v) ? "—" : v;
        }
        catch { return "—"; }
    }

    private static List<ManagementObject> Query(string wql, string scope = @"root\cimv2")
    {
        using var searcher = new ManagementObjectSearcher(scope, wql);
        return searcher.Get().Cast<ManagementObject>().ToList();
    }

    private static string S(ManagementBaseObject mo, string prop)
    {
        try
        {
            var v = mo[prop]?.ToString()?.Trim();
            return string.IsNullOrEmpty(v) ? "—" : v;
        }
        catch (ManagementException) { return "—"; }
    }

    internal static string Bytes(double b)
    {
        if (b <= 0) return "—";
        string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
        int i = 0;
        while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
        return string.Format(Culture, "{0:0.##} {1}", b, u[i]);
    }

    private static string Bytes(object? v) => v == null ? "—" : Bytes(Convert.ToDouble(v));

    private static string YesNo(object? v) => v is bool b ? T(b ? "hw.common.yes" : "hw.common.no") : "—";

    private static string WmiDate(object? v)
    {
        try { return v == null ? "—" : ManagementDateTimeConverter.ToDateTime(v.ToString()).ToString("d", Culture); }
        catch { return "—"; }
    }

    // ------------------------------------------------------------------ CPU

    private static void CollectCpu(List<InfoNode> nodes)
    {
        foreach (var c in Query("SELECT * FROM Win32_Processor"))
        {
            var cpu = new InfoNode { Label = S(c, "Name") };
            cpu.Add(T("hw.common.manufacturer"), S(c, "Manufacturer"));
            cpu.Add(T("hw.cpu.id"), S(c, "Caption"));
            cpu.Add(T("hw.cpu.socket"), S(c, "SocketDesignation"));
            cpu.Add(T("hw.cpu.arch"), RuntimeInformation.OSArchitecture.ToString());
            cpu.Add(T("hw.cpu.cores"), S(c, "NumberOfCores"));
            cpu.Add(T("hw.cpu.logical"), S(c, "NumberOfLogicalProcessors"));
            cpu.Add(T("hw.cpu.maxClock"), S(c, "MaxClockSpeed") + " MHz");
            cpu.Add(T("hw.cpu.currentClock"), S(c, "CurrentClockSpeed") + " MHz");
            cpu.Add(T("hw.cpu.l2"), KbOrDash(c["L2CacheSize"]));
            cpu.Add(T("hw.cpu.l3"), KbOrDash(c["L3CacheSize"]));
            cpu.Add(T("hw.cpu.virt"), YesNo(c["VirtualizationFirmwareEnabled"]));
            nodes.Add(cpu);
        }
        var isa = new InfoNode { Label = T("hw.cpu.instructions") };
        isa.Add(T("hw.cpu.instructions"), string.Join(", ", MachineInfo.InstructionSets()));
        nodes.Add(isa);
    }

    private static string KbOrDash(object? kb)
    {
        double v = Convert.ToDouble(kb ?? 0);
        return v > 0 ? Bytes(v * 1024) : "—";
    }

    // ------------------------------------------------------------------ Placa-mãe / BIOS

    private static void CollectBoard(List<InfoNode> nodes)
    {
        var board = new InfoNode { Label = T("hw.board.board") };
        foreach (var b in Query("SELECT * FROM Win32_BaseBoard"))
        {
            board.Add(T("hw.common.manufacturer"), S(b, "Manufacturer"));
            board.Add(T("hw.common.model"), S(b, "Product"));
            board.Add(T("hw.common.version"), S(b, "Version"));
            board.Add(T("hw.common.serial"), S(b, "SerialNumber"), sensitive: true);
        }
        nodes.Add(board);

        var sys = new InfoNode { Label = T("hw.board.system") };
        foreach (var cs in Query("SELECT Manufacturer, Model, SystemType FROM Win32_ComputerSystem"))
        {
            sys.Add(T("hw.common.manufacturer"), S(cs, "Manufacturer"));
            sys.Add(T("hw.common.model"), S(cs, "Model"));
            sys.Add(T("hw.common.type"), S(cs, "SystemType"));
        }
        nodes.Add(sys);

        var bios = new InfoNode { Label = "BIOS" };
        foreach (var b in Query("SELECT * FROM Win32_BIOS"))
        {
            bios.Add(T("hw.common.manufacturer"), S(b, "Manufacturer"));
            bios.Add(T("hw.common.version"), S(b, "SMBIOSBIOSVersion"));
            bios.Add(T("hw.common.date"), WmiDate(b["ReleaseDate"]));
            bios.Add("SMBIOS", $"{S(b, "SMBIOSMajorVersion")}.{S(b, "SMBIOSMinorVersion")}");
        }
        bios.Add(T("hw.bios.mode"), FirmwareMode());
        bios.Add("Secure Boot", SecureBoot());
        nodes.Add(bios);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFirmwareType(out uint firmwareType);

    private static string FirmwareMode()
    {
        try
        {
            if (GetFirmwareType(out uint t))
                return t switch { 1 => "Legacy BIOS", 2 => "UEFI", _ => T("hw.common.unknown") };
        }
        catch { }
        return T("hw.common.unknown");
    }

    private static string SecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            if (key?.GetValue("UEFISecureBootEnabled") is int v)
                return T(v == 1 ? "hw.common.enabled" : "hw.common.disabled");
        }
        catch { }
        return T("hw.common.unknown");
    }

    // ------------------------------------------------------------------ Memória

    private static string RamType(object? smbios) => Convert.ToInt32(smbios ?? 0) switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 27 => "LPDDR", 28 => "LPDDR2", 29 => "LPDDR3",
        30 => "LPDDR4", 34 => "DDR5", 35 => "LPDDR5",
        _ => T("hw.common.unknown"),
    };

    private static string RamForm(object? ff) => Convert.ToInt32(ff ?? 0) switch
    {
        8 => "DIMM", 12 => "SO-DIMM", 13 => "Micro-DIMM", 15 => "FB-DIMM", 9 => "RIMM", 11 => "SRIMM",
        6 => T("hw.mem.form.proprietary"), 0 => "—",
        _ => T("hw.mem.form.other"),
    };

    private static string Volt(object? mv)
    {
        if (mv == null) return "—";
        double v = Convert.ToDouble(mv);
        return v > 0 ? string.Format(Culture, "{0:0.00#} V", v / 1000) : "—";
    }

    private sealed record RamModule(string Bank, string Loc, ulong Cap, string Type, string Form, int Rated, int Conf,
                                    string Maker, string Part, string Serial, int DataW, int TotalW, string Rank,
                                    string VoltCfg, string VoltRange);

    private static void CollectRam(List<InfoNode> nodes)
    {
        var mods = new List<RamModule>();
        foreach (var m in Query("SELECT * FROM Win32_PhysicalMemory"))
        {
            int attr = Convert.ToInt32(m["Attributes"] ?? 0);
            string rank = attr switch { 1 => "1 (single)", 2 => "2 (dual)", 3 => "3", 4 => "4 (quad)", _ => "—" };
            string range = m["MinVoltage"] != null && m["MaxVoltage"] != null
                ? $"{Volt(m["MinVoltage"])} – {Volt(m["MaxVoltage"])}" : "—";
            mods.Add(new RamModule(S(m, "BankLabel"), S(m, "DeviceLocator"), Convert.ToUInt64(m["Capacity"] ?? 0UL),
                                   RamType(m["SMBIOSMemoryType"]), RamForm(m["FormFactor"]),
                                   Convert.ToInt32(m["Speed"] ?? 0), Convert.ToInt32(m["ConfiguredClockSpeed"] ?? 0),
                                   S(m, "Manufacturer"), S(m, "PartNumber"), S(m, "SerialNumber"),
                                   Convert.ToInt32(m["DataWidth"] ?? 0), Convert.ToInt32(m["TotalWidth"] ?? 0),
                                   rank, Volt(m["ConfiguredVoltage"]), range));
        }

        ulong total = 0;
        foreach (var m in mods) total += m.Cap;

        int slots = 0;
        ulong maxBytes = 0;
        try
        {
            foreach (var a in Query("SELECT * FROM Win32_PhysicalMemoryArray"))
            {
                slots += Convert.ToInt32(a["MemoryDevices"] ?? 0);
                ulong kb = a["MaxCapacityEx"] != null ? Convert.ToUInt64(a["MaxCapacityEx"]) : Convert.ToUInt64(a["MaxCapacity"] ?? 0UL);
                maxBytes += kb * 1024;
            }
        }
        catch { }

        var sum = new InfoNode { Label = T("hw.mem.summary") };
        nodes.Add(sum);
        sum.Add(T("hw.mem.total"), Bytes(total));
        sum.Add(T("hw.mem.modules"), slots > 0 ? T("hw.mem.slotsOf", mods.Count, slots) : mods.Count.ToString(Culture));
        if (maxBytes > 0) sum.Add(T("hw.mem.maxCapacity"), Bytes(maxBytes));
        if (mods.Count > 0)
        {
            sum.Add(T("hw.common.type"), string.Join(" / ", mods.Select(m => m.Type).Distinct()));

            int conf = mods.Where(m => m.Conf > 0).Select(m => m.Conf).DefaultIfEmpty(0).Min();
            int rated = mods.Select(m => m.Rated).DefaultIfEmpty(0).Max();
            sum.Add(T("hw.mem.speedInUse"), conf > 0 ? $"{conf} MT/s" : "—");
            sum.Add(T("hw.mem.speedRated"), rated > 0 ? $"{rated} MT/s" : "—");

            int label = MemoryHeuristics.SpeedFromPartNumbers(mods.Select(m => m.Part));
            sum.Add(T("hw.mem.profile"), MemoryHeuristics.XmpVerdict(conf, rated, label));
            sum.Add(T("hw.mem.channels"), MemoryHeuristics.ChannelVerdict(mods.Select(m => (m.Loc, m.Bank)).ToList()));
            sum.Add("ECC", mods.Any(m => m.TotalW > m.DataW && m.DataW > 0) ? T("hw.mem.ecc.yes") : T("hw.common.no"));

            var volts = mods.Select(m => m.VoltCfg).Where(v => v != "—").Distinct().ToList();
            if (volts.Count > 0) sum.Add(T("hw.mem.voltage"), string.Join(" / ", volts));
        }
        sum.Add(T("hw.mem.timings"), T("hw.mem.timings.value"));

        try
        {
            foreach (var os in Query("SELECT TotalVisibleMemorySize, FreePhysicalMemory, TotalVirtualMemorySize, FreeVirtualMemory FROM Win32_OperatingSystem"))
            {
                double totKb = Convert.ToDouble(os["TotalVisibleMemorySize"]), freeKb = Convert.ToDouble(os["FreePhysicalMemory"]);
                double virtTot = Convert.ToDouble(os["TotalVirtualMemorySize"]), virtFree = Convert.ToDouble(os["FreeVirtualMemory"]);
                if (totKb > 0)
                    sum.Add(T("hw.mem.inUse"), T("hw.mem.inUse.value", Bytes((totKb - freeKb) * 1024), Bytes(totKb * 1024),
                                                  ((totKb - freeKb) / totKb).ToString("P0", Culture)));
                if (virtTot > 0)
                    sum.Add(T("hw.mem.virtual"), T("hw.mem.inUse.value", Bytes((virtTot - virtFree) * 1024), Bytes(virtTot * 1024),
                                                    ((virtTot - virtFree) / virtTot).ToString("P0", Culture)));
            }
        }
        catch { }

        foreach (var m in mods)
        {
            var slot = new InfoNode { Label = T("hw.mem.module", $"{m.Bank} / {m.Loc}") };
            slot.Add(T("hw.common.capacity"), Bytes(m.Cap));
            slot.Add(T("hw.common.type"), m.Type);
            slot.Add(T("hw.mem.form"), m.Form);
            slot.Add("Ranks", m.Rank);
            slot.Add(T("hw.mem.speedInUse"), m.Conf > 0 ? $"{m.Conf} MT/s" : "—");
            slot.Add(T("hw.mem.speedRatedShort"), m.Rated > 0 ? $"{m.Rated} MT/s" : "—");
            slot.Add(T("hw.mem.voltage"), m.VoltCfg);
            slot.Add(T("hw.mem.voltRange"), m.VoltRange);
            slot.Add(T("hw.mem.width"), m.DataW > 0 ? $"{m.DataW} / {m.TotalW} bits" : "—");
            slot.Add(T("hw.common.manufacturer"), m.Maker);
            slot.Add("Part number", m.Part);
            slot.Add(T("hw.common.serial"), m.Serial, sensitive: true);
            nodes.Add(slot);
        }
    }

    // ------------------------------------------------------------------ GPU

    private static void CollectGpu(List<InfoNode> nodes)
    {
        var vramByName = ReadVramFromRegistry();
        foreach (var g in Query("SELECT * FROM Win32_VideoController"))
        {
            string name = S(g, "Name");
            var gpu = new InfoNode { Label = name };
            gpu.Add(T("hw.gpu.vendor"), S(g, "AdapterCompatibility"));
            gpu.Add(T("hw.gpu.processor"), S(g, "VideoProcessor"));
            // O WMI limita AdapterRAM a 4 GB; o registro do driver traz o valor real (qwMemorySize).
            gpu.Add(T("hw.gpu.vram"), vramByName.TryGetValue(name, out var vram) && vram > 0 ? Bytes(vram) : Bytes(g["AdapterRAM"]));
            gpu.Add(T("hw.gpu.driver"), S(g, "DriverVersion"));
            gpu.Add(T("hw.gpu.driverDate"), WmiDate(g["DriverDate"]));
            gpu.Add(T("hw.gpu.resolution"),
                    $"{S(g, "CurrentHorizontalResolution")} × {S(g, "CurrentVerticalResolution")} @ {S(g, "CurrentRefreshRate")} Hz");
            gpu.Add("PNP ID", S(g, "PNPDeviceID"), sensitive: true);
            nodes.Add(gpu);
        }
    }

    private static Dictionary<string, double> ReadVramFromRegistry()
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return result;
            foreach (var sub in cls.GetSubKeyNames().Where(n => n.All(char.IsDigit)))
            {
                try
                {
                    using var k = cls.OpenSubKey(sub);
                    if (k?.GetValue("DriverDesc") is not string desc) continue;
                    object? raw = k.GetValue("HardwareInformation.qwMemorySize") ?? k.GetValue("HardwareInformation.MemorySize");
                    double bytes = raw switch
                    {
                        long l => l,
                        int i => (uint)i,
                        byte[] b when b.Length >= 8 => BitConverter.ToInt64(b, 0),
                        byte[] b when b.Length >= 4 => BitConverter.ToUInt32(b, 0),
                        _ => 0,
                    };
                    if (bytes > 0) result[desc] = Math.Max(bytes, result.GetValueOrDefault(desc));
                }
                catch (System.Security.SecurityException) { }
            }
        }
        catch { }
        return result;
    }

    // ------------------------------------------------------------------ Armazenamento

    private static void CollectDisks(List<InfoNode> nodes)
    {
        // MSFT_PhysicalDisk diz se é SSD/HDD, o barramento (NVMe/SATA/USB) e a saúde.
        var physical = new List<ManagementObject>();
        try { physical = Query("SELECT FriendlyName, MediaType, BusType, HealthStatus, SerialNumber FROM MSFT_PhysicalDisk", @"root\Microsoft\Windows\Storage"); }
        catch { }

        var health = new Dictionary<int, DiskHealth>();
        try
        {
            foreach (var h in DiskInfo.ReadHealth()) health[h.DiskNumber] = h;
        }
        catch { }

        foreach (var d in Query("SELECT * FROM Win32_DiskDrive"))
        {
            string model = S(d, "Model");
            string serial = S(d, "SerialNumber");
            var disk = new InfoNode { Label = model };
            disk.Add(T("hw.common.capacity"), Bytes(d["Size"]));

            var pd = physical.FirstOrDefault(p => S(p, "SerialNumber") == serial && serial != "—")
                     ?? physical.FirstOrDefault(p => S(p, "FriendlyName") == model);
            if (pd != null)
            {
                disk.Add(T("hw.disk.media"), Convert.ToInt32(pd["MediaType"] ?? 0) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => T("hw.common.unknown") });
                disk.Add(T("hw.disk.bus"), BusType(Convert.ToInt32(pd["BusType"] ?? 0)));
                disk.Add(T("hw.disk.health"), Convert.ToInt32(pd["HealthStatus"] ?? 5) switch
                {
                    0 => T("hw.disk.health.ok"), 1 => T("hw.disk.health.warn"), 2 => T("hw.disk.health.bad"), _ => T("hw.common.unknown"),
                });
            }
            else
            {
                disk.Add(T("hw.disk.interface"), S(d, "InterfaceType"));
            }
            if (health.TryGetValue(Convert.ToInt32(d["Index"] ?? -1), out var hd) && hd.Status != HealthStatus.Unknown)
            {
                disk.Add(T("hw.disk.smart"), DiskInfo.StatusText(hd));
                if (hd.TemperatureC is { } temp) disk.Add(T("health.temperature"), $"{temp} °C");
                if (hd.PowerOnHours is { } hours) disk.Add(T("health.powerOnHours"), T("health.hours", hours.ToString("N0", Culture)));
                if (hd.HostWrittenBytes is { } written) disk.Add(T("health.written"), Bytes(written));
            }
            disk.Add(T("hw.disk.partitions"), S(d, "Partitions"));
            disk.Add("Firmware", S(d, "FirmwareRevision"));
            disk.Add(T("hw.common.serial"), serial, sensitive: true);
            nodes.Add(disk);
        }

        var volumes = new InfoNode { Label = T("hw.disk.volumes") };
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (!drive.IsReady || drive.TotalSize <= 0) continue;
                string label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? drive.Name : $"{drive.Name} ({drive.VolumeLabel})";
                volumes.Add(label, T("hw.disk.volume.value", Bytes(drive.AvailableFreeSpace), Bytes(drive.TotalSize),
                                     $"{drive.DriveFormat}, {drive.DriveType}"));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        if (volumes.IsGroup) nodes.Add(volumes);
    }

    private static string BusType(int t) => t switch
    {
        1 => "SCSI", 2 => "ATAPI", 3 => "ATA", 4 => "IEEE 1394", 5 => "SSA", 6 => "Fibre Channel", 7 => "USB", 8 => "RAID",
        9 => "iSCSI", 10 => "SAS", 11 => "SATA", 12 => "SD", 13 => "MMC", 15 => "File-backed", 16 => "Storage Spaces",
        17 => "NVMe", 18 => "SCM", 19 => "UFS",
        _ => T("hw.common.unknown"),
    };

    // ------------------------------------------------------------------ Sistema operacional

    private static void CollectOs(List<InfoNode> nodes)
    {
        var n = new InfoNode { Label = "Windows" };
        foreach (var os in Query("SELECT * FROM Win32_OperatingSystem"))
        {
            n.Label = S(os, "Caption").Replace("Microsoft ", "");
            n.Add(T("hw.common.version"), S(os, "Version"));
            n.Add("Build", S(os, "BuildNumber"));
            n.Add(T("hw.cpu.arch"), S(os, "OSArchitecture"));
            n.Add(T("hw.os.installed"), WmiDate(os["InstallDate"]));
        }
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        n.Add(T("hw.os.uptime"), T("hw.os.uptime.value", (int)up.TotalDays, up.Hours, up.Minutes));
        n.Add(T("hw.os.computer"), Environment.MachineName, sensitive: true);
        n.Add(T("hw.os.admin"), YesNo(Elevation.IsElevated));
        n.Add(".NET", RuntimeInformation.FrameworkDescription);
        nodes.Add(n);
    }
}
