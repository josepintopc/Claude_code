using System.Globalization;
using System.Runtime.Versioning;
using System.Text.Json;
using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Cpu;
using SupernovaBench.Core.Benchmarks.Disk;
using SupernovaBench.Core.Benchmarks.Memory;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;
using SupernovaBench.Hardware;

namespace SupernovaBench.Cli;

/// <summary>Comandos da Fase 3: memória, disco, stress de CPU/RAM e saúde dos discos.</summary>
internal static class Phase3Commands
{
    public static int BenchMemory(bool quick, bool json, bool save, CancellationToken ct)
    {
        var bench = new MemoryBench(quick);
        if (!json)
        {
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} — {Loc.T(TestIds.NameKey(TestIds.MemoryBench))}");
            var c = bench.Caches;
            Console.WriteLine($"{MachineInfo.CpuName()} · {bench.Threads} threads · L1 {c.L1 >> 10} KB · L2 {c.L2 >> 10} KB · L3 {c.L3Total >> 20} MB");
            bench.Measured = (level, op, v) =>
                Console.WriteLine($"  {MemoryBench.LevelId(level),-4} {MemoryBench.OpId(op),-8} {v.ToString("0.0", Loc.Culture),10} {(op == MemOp.Latency ? "ns" : "GB/s")}");
        }
        var r = bench.Run(ct);
        if (r == null) { Console.WriteLine(Loc.T("cpu.cancelled")); return 2; }
        var result = bench.ToResult(r, null);
        if (save) new ResultHistory().Add(result);
        if (json) Console.WriteLine(JsonSerializer.Serialize(result, JsonFile.Options));
        else Console.WriteLine($"  {Loc.T("cpu.score")}: {result.Score.ToString("N0", Loc.Culture)} pts  ({result.Seconds.ToString("N1", Loc.Culture)} s)");
        return 0;
    }

    public static async Task<int> BenchDisk(string? drive, string? path, string? size, int runs, double seconds, bool json, bool save, CancellationToken ct)
    {
        string dir = path ?? (drive != null ? drive.TrimEnd(':', '\\', '/') + ":\\" : Path.GetTempPath());
        long bytes = ParseSize(size) ?? (1L << 30);
        var bench = new DiskBench(new DiskBenchOptions
        {
            Directory = dir, FileSize = bytes, Runs = runs, TestDuration = TimeSpan.FromSeconds(seconds),
            Interval = seconds >= 3 ? TimeSpan.FromSeconds(1) : TimeSpan.Zero,
        });
        if (!json)
        {
            Console.WriteLine($"{AppInfo.Name} {AppInfo.Version} — {Loc.T(TestIds.NameKey(TestIds.DiskBench))}");
            Console.WriteLine($"{Path.GetFullPath(dir)} · {DiskBench.SizeId(bytes)} · {runs}× · {seconds:0.#} s");
            bench.Measured = (spec, write, m) =>
                Console.WriteLine($"  {spec.Label,-12} {(write ? "write" : "read "),-6} {m.MBps,10:0.00} MB/s {m.Iops,10:0} IOPS {m.LatencyUs,9:0.0} µs");
        }

        DiskBenchResult? r;
        try { r = await bench.RunAsync(ct); }
        catch (DiskBenchException ex)
        {
            Console.Error.WriteLine(ex.Key == "space" ? Loc.T("disk.error.space", DiskBench.SizeId(bytes)) : Loc.T("disk.error.access", ex.Message));
            return 5;
        }
        if (r == null) { Console.WriteLine(Loc.T("cpu.cancelled")); return 2; }

        string? device = null;
        if (OperatingSystem.IsWindows()) device = DeviceFor(dir);
        var result = bench.ToResult(r, device);
        if (save) new ResultHistory().Add(result);
        if (json) Console.WriteLine(JsonSerializer.Serialize(result, JsonFile.Options));
        else Console.WriteLine($"  {Loc.T("cpu.score")}: {result.Score.ToString("N0", Loc.Culture)} pts  ({result.Seconds.ToString("N1", Loc.Culture)} s)");
        return 0;
    }

    [SupportedOSPlatform("windows")]
    private static string? DeviceFor(string dir)
    {
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(dir)) ?? "";
            return DiskInfo.Drives().FirstOrDefault(d => string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase))?.DeviceName;
        }
        catch
        {
            return null;
        }
    }

    public static int StressCpu(string? mode, int? threads, double seconds)
    {
        var m = mode switch { "render" => CpuStressMode.Render, "int" => CpuStressMode.Integer, _ => CpuStressMode.Fma };
        var s = new CpuStress();
        int n = threads ?? Environment.ProcessorCount;
        Console.WriteLine($"{Loc.T("cpustress.title")} · {m} · {n} threads · {seconds:0.#} s");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; s.Stop(); };
        s.Start(m, n, TimeSpan.FromSeconds(seconds));
        while (s.Running)
        {
            Thread.Sleep(500);
            Console.Write($"\r  {Formats(s.Elapsed)} · {s.Iterations:N0} · {Loc.T("cpustress.errors")}: {s.Errors}   ");
        }
        s.Join();
        Console.WriteLine();
        if (s.Error != null) { Console.Error.WriteLine(s.Error); return 1; }
        Console.WriteLine(s.Errors == 0 ? Loc.T("cpustress.status.passed") : Loc.T("cpustress.status.errors", s.Errors, s.FirstErrorThread ?? 1));
        return s.Errors == 0 ? 0 : 4;
    }

    public static int StressRam(long? mb, int? percent, double? seconds)
    {
        var (_, available) = MemoryStatus.Read();
        long bytes = mb is { } v ? v << 20 : available * (percent ?? 50) / 100;
        var s = new RamStress();
        Console.WriteLine($"{Loc.T("ramstress.title")} · {bytes >> 20} MB · {Environment.ProcessorCount} threads");
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; s.Stop(); };
        s.Start(bytes, Environment.ProcessorCount, seconds is { } sec ? TimeSpan.FromSeconds(sec) : null, seconds == null ? 1 : null);
        while (s.Running)
        {
            Thread.Sleep(500);
            Console.Write($"\r  {s.Coverage,6:0.0}% · {s.MBps,8:N0} MB/s · {s.CurrentPattern,-10} · {Loc.T("ramstress.errors")}: {s.ErrorCount}   ");
        }
        s.Join();
        Console.WriteLine();
        if (s.Error != null) { Console.Error.WriteLine(s.Error == "out-of-memory" ? Loc.T("ramstress.oom") : s.Error); return 1; }
        foreach (var e in s.Errors.Take(10))
            Console.WriteLine($"  0x{e.Offset:X} · 0x{e.Expected:X16} → 0x{e.Actual:X16} · {e.Pattern}");
        string cov = string.Format(Loc.Culture, "{0:0.0}%", s.Coverage);
        Console.WriteLine(s.ErrorCount > 0 ? Loc.T("ramstress.verdict.failed", s.ErrorCount)
                          : s.Coverage >= 99.99 ? Loc.T("ramstress.verdict.passed", cov) : Loc.T("ramstress.verdict.incomplete", cov));
        return s.ErrorCount == 0 ? 0 : 4;
    }

    [SupportedOSPlatform("windows")]
    public static int DiskHealthInfo()
    {
        var disks = DiskInfo.ReadHealth();
        if (disks.Count == 0) Console.WriteLine(Loc.T("health.noDisks"));
        foreach (var d in disks)
        {
            Console.WriteLine($"[{d.DiskNumber}] {d.Model} · {d.Bus} {d.Media} · {DiskInfo.StatusText(d)}");
            Console.WriteLine($"    {Loc.T("health.temperature")}: {(d.TemperatureC is { } t ? $"{t} °C" : "—")} · " +
                              $"{Loc.T("health.powerOnHours")}: {d.PowerOnHours?.ToString("N0", Loc.Culture) ?? "—"} h · " +
                              $"{Loc.T("health.source")}: {(d.Source == "" ? "—" : d.Source)}");
            foreach (var n in d.Notes) Console.WriteLine("    ! " + Loc.T(n.Key, n.Value));
            if (d.UnavailableKey != null) Console.WriteLine("    " + Loc.T(d.UnavailableKey));
        }
        return 0;
    }

    internal static long? ParseSize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Trim().ToLowerInvariant();
        long mult = s.EndsWith('g') ? 1L << 30 : s.EndsWith('m') ? 1L << 20 : 1;
        return double.TryParse(s.TrimEnd('g', 'm', 'b'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? (long)(v * mult) : null;
    }

    private static string Formats(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
}
