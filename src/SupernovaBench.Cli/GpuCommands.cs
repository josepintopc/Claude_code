using System.Runtime.Versioning;
using System.Text.Json;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;
using SupernovaBench.Graphics;
using SupernovaBench.Graphics.Native;

namespace SupernovaBench.Cli;

[SupportedOSPlatform("windows")]
internal static class GpuCommands
{
    public static int Info()
    {
        Console.WriteLine(Loc.T("cli.gpu.adapters") + ":");
        foreach (var a in GpuAdapters.Enumerate(includeSoftware: true))
        {
            string vram = a.DedicatedVideoMemory > 0 ? $"{a.DedicatedVideoMemory / (double)(1UL << 30):0.0} GB" : "—";
            Console.WriteLine($"  [{a.Index}] {a.Name} · {a.VendorName} {a.VendorHex}:{a.DeviceHex} · VRAM {vram} · D3D {a.FeatureLevel ?? "—"}" +
                              (a.IsSoftware ? " · software" : ""));
            var spec = GpuSpecDb.Find(a.VendorHex, a.DeviceHex, a.Name);
            if (spec != null) Console.WriteLine($"      {spec.Chip} · {spec.Arch} · {spec.Shaders} shaders · {spec.MemType} · TDP {spec.TdpW} W");
        }
        Console.WriteLine($"NVML: {(Nvml.Available ? $"{Nvml.Devices().Count} · driver {Nvml.DriverVersion()}" : "—")}");
        Console.WriteLine(Loc.T("cli.gpu.specs", GpuSpecDb.Count));
        return 0;
    }

    public static int Bench(string? resId, int seconds, bool warp, bool headless, bool json, bool save)
    {
        var res = GpuBenchmark.ResolutionById(resId);
        // No WARP (software) a cena oficial levaria minutos por frame: usa resolução e detalhe reduzidos.
        var bench = warp
            ? new GameSceneBench
            {
                Width = 320, Height = 180, Duration = TimeSpan.FromSeconds(seconds), Warmup = TimeSpan.FromSeconds(0.5),
                UseWarp = true, Headless = headless, TreeCount = 400, TerrainResolution = 64, ShadowResolution = 512,
            }
            : new GameSceneBench { Width = res.Width, Height = res.Height, Duration = TimeSpan.FromSeconds(seconds), Headless = headless };

        Console.CancelKeyPress += (_, e) => { e.Cancel = true; bench.Cancel(); };
        if (!json) Console.WriteLine($"{Loc.T("test.gpu.scene")} · {bench.Width}x{bench.Height} · {seconds} s{(warp ? " · WARP" : "")}");

        GpuSceneResult? result = null;
        var thread = new Thread(() => result = bench.Run()) { IsBackground = true };
        thread.Start();
        while (!thread.Join(1000))
            if (!json) Console.Write($"\r  {Loc.T("cpu.progress")}: {bench.Progress * 100,5:0.0}%  ·  {bench.CurrentFps,6:0.0} FPS   ");

        if (result == null)
        {
            Console.WriteLine();
            Console.Error.WriteLine(bench.Cancelled ? Loc.T("cpu.cancelled") : Loc.T("cpu.failed", bench.Error ?? "?"));
            return 2;
        }

        // Resultado do WARP não entra no ranking oficial (preset diferente).
        var record = GpuBenchmark.ToResult(result, warp ? $"warp-{seconds}s" : GpuBenchmark.PresetId(res, seconds));
        if (save) new ResultHistory().Add(record);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(record, JsonFile.Options));
        }
        else
        {
            Console.WriteLine();
            Console.WriteLine($"  {result.Adapter}");
            Console.WriteLine($"  {Loc.T("cpu.score")}: {record.Score.ToString("N0", Loc.Culture)} pts  ·  {Loc.T("gpu.avgFps")}: {result.AvgFps.ToString("N1", Loc.Culture)}  ·  {Loc.T("gpu.low1")}: {result.Low1Fps.ToString("N1", Loc.Culture)}  ({result.Frames} frames)");
        }
        return 0;
    }
}
