using System.Runtime.InteropServices;

namespace SupernovaBench.Core.Info;

/// <summary>
/// Tamanhos de cache da CPU. <see cref="L1"/> e <see cref="L2"/> são por núcleo; <see cref="L3"/> é o de um bloco
/// (CCD/anel) e <see cref="L3Total"/> a soma de todos. <see cref="Cores"/> conta núcleos físicos.
/// </summary>
public sealed record CacheSizes(long L1, long L2, long L3, long L3Total, int Cores, bool Detected)
{
    public static readonly CacheSizes Fallback = new(48 * 1024, 1024 * 1024, 32L << 20, 32L << 20, Math.Max(1, Environment.ProcessorCount / 2), false);

    /// <summary>Threads lógicas por núcleo físico (2 com SMT/Hyper-Threading).</summary>
    public int ThreadsPerCore => Math.Max(1, (int)Math.Round((double)Environment.ProcessorCount / Math.Max(1, Cores)));
}

/// <summary>Detecta os tamanhos de cache (Windows: GetLogicalProcessorInformationEx; Linux: /sys).</summary>
public static class CacheInfo
{
    private static CacheSizes? _cached;

    public static CacheSizes Detect()
    {
        if (_cached != null) return _cached;
        CacheSizes? found = null;
        try
        {
            if (OperatingSystem.IsWindows()) found = FromWindows();
            else if (OperatingSystem.IsLinux()) found = FromLinux();
        }
        catch
        {
            // Formato inesperado: usa os valores padrão.
        }
        return _cached = found ?? CacheSizes.Fallback;
    }

    // ------------------------------------------------------------------ Windows

    private const int RelationProcessorCore = 0, RelationCache = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);

    private static CacheSizes? FromWindows()
    {
        var caches = new List<(int Level, int Type, long Size)>();
        foreach (var (rel, ptr) in Entries(RelationCache))
        {
            if (rel != RelationCache) continue;
            // CACHE_RELATIONSHIP começa no offset 8: Level (1), Associativity (1), LineSize (2), CacheSize (4), Type (4).
            int level = Marshal.ReadByte(ptr, 8);
            long size = (uint)Marshal.ReadInt32(ptr, 12);
            int type = Marshal.ReadInt32(ptr, 16);
            caches.Add((level, type, size));
        }
        int cores = Entries(RelationProcessorCore).Count(e => e.Rel == RelationProcessorCore);
        return Build(caches, cores);
    }

    private static List<(int Rel, IntPtr Ptr)> Entries(int relationship)
    {
        var list = new List<(int, IntPtr)>();
        uint len = 0;
        GetLogicalProcessorInformationEx(relationship, IntPtr.Zero, ref len);
        if (len == 0) return list;
        // O buffer fica vivo até o fim do processo (é pequeno e lido uma vez).
        IntPtr buf = Marshal.AllocHGlobal((int)len);
        if (!GetLogicalProcessorInformationEx(relationship, buf, ref len)) return list;
        long offset = 0;
        while (offset < len)
        {
            IntPtr p = buf + (int)offset;
            int rel = Marshal.ReadInt32(p, 0);
            int size = Marshal.ReadInt32(p, 4);
            if (size <= 0) break;
            list.Add((rel, p));
            offset += size;
        }
        return list;
    }

    // ------------------------------------------------------------------ Linux

    private static CacheSizes? FromLinux()
    {
        var caches = new List<(int Level, int Type, long Size)>();
        var seen = new HashSet<string>();
        foreach (var cpu in Directory.EnumerateDirectories("/sys/devices/system/cpu", "cpu*"))
        {
            var dir = Path.Combine(cpu, "cache");
            if (!Directory.Exists(dir)) continue;
            foreach (var idx in Directory.EnumerateDirectories(dir, "index*"))
            {
                string Read(string f) => File.ReadAllText(Path.Combine(idx, f)).Trim();
                int level = int.Parse(Read("level"));
                string type = Read("type");
                long size = ParseSize(Read("size"));
                string shared = File.Exists(Path.Combine(idx, "shared_cpu_list")) ? Read("shared_cpu_list") : cpu;
                if (!seen.Add($"{level}|{type}|{shared}")) continue;
                caches.Add((level, type switch { "Data" => 2, "Instruction" => 1, _ => 0 }, size));
            }
        }
        int cores = 0;
        try
        {
            var ids = new HashSet<string>();
            string phys = "0";
            foreach (var line in File.ReadLines("/proc/cpuinfo"))
            {
                if (line.StartsWith("physical id", StringComparison.Ordinal)) phys = line.Split(':', 2)[1].Trim();
                else if (line.StartsWith("core id", StringComparison.Ordinal)) ids.Add(phys + "|" + line.Split(':', 2)[1].Trim());
            }
            cores = ids.Count;
        }
        catch { }
        return Build(caches, cores);
    }

    internal static long ParseSize(string s)
    {
        s = s.Trim().ToUpperInvariant();
        long mult = s.EndsWith('K') ? 1024 : s.EndsWith('M') ? 1024 * 1024 : s.EndsWith('G') ? 1L << 30 : 1;
        return long.Parse(s.TrimEnd('K', 'M', 'G', 'B')) * mult;
    }

    /// <summary>Monta o resultado: L1 de dados e L2 pelo menor (cabe em qualquer núcleo), L3 pelo maior bloco.</summary>
    internal static CacheSizes? Build(IReadOnlyList<(int Level, int Type, long Size)> caches, int cores)
    {
        var fb = CacheSizes.Fallback;
        long Min(int level) => caches.Where(c => c.Level == level && c.Type != 1 && c.Size > 0).Select(c => c.Size).DefaultIfEmpty(0).Min();
        long l1 = Min(1), l2 = Min(2);
        var l3s = caches.Where(c => c.Level == 3 && c.Size > 0).Select(c => c.Size).ToList();
        if (l1 == 0 && l2 == 0 && l3s.Count == 0) return null;
        long l3 = l3s.DefaultIfEmpty(0).Max();
        long l3Total = l3s.Sum();
        return new CacheSizes(l1 > 0 ? l1 : fb.L1, l2 > 0 ? l2 : fb.L2, l3, l3Total, cores > 0 ? cores : fb.Cores, true);
    }
}

/// <summary>Memória física total e disponível (Windows: GlobalMemoryStatusEx; Linux: /proc/meminfo).</summary>
public static class MemoryStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

    /// <summary>(total, disponível) em bytes.</summary>
    public static (long Total, long Available) Read()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var st = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
                if (GlobalMemoryStatusEx(ref st)) return ((long)st.ullTotalPhys, (long)st.ullAvailPhys);
            }
            else if (File.Exists("/proc/meminfo"))
            {
                long total = 0, avail = 0;
                foreach (var line in File.ReadLines("/proc/meminfo"))
                {
                    var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2) continue;
                    if (parts[0] == "MemTotal:") total = long.Parse(parts[1]) * 1024;
                    else if (parts[0] == "MemAvailable:") avail = long.Parse(parts[1]) * 1024;
                }
                if (total > 0) return (total, avail > 0 ? avail : total / 2);
            }
        }
        catch { }
        long gc = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (gc, gc / 2);
    }
}
