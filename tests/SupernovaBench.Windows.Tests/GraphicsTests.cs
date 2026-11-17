using System.Runtime.Versioning;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Graphics;
using SupernovaBench.Graphics.Native;
using Vortice.D3DCompiler;

namespace SupernovaBench.Windows.Tests;

public class SpecAndParsingTests
{
    [Fact]
    public void Spec_db_finds_cards_and_uses_name_to_pick_variant()
    {
        var rtx4090 = GpuSpecDb.Find("10DE", "2684", "NVIDIA GeForce RTX 4090");
        Assert.NotNull(rtx4090);
        Assert.Equal("AD102", rtx4090!.Chip);
        Assert.Equal(16384, rtx4090.Shaders);

        Assert.Equal(4096, GpuSpecDb.Find("1002", "7550", "AMD Radeon RX 9070 XT")!.Shaders);
        Assert.Equal(3584, GpuSpecDb.Find("1002", "7550", "AMD Radeon RX 9070")!.Shaders);
        Assert.True(GpuSpecDb.Find("1002", "164E", "AMD Radeon(TM) Graphics")!.IsIntegrated);
        Assert.Null(GpuSpecDb.Find("8086", "56A0", "Intel Arc A770"));
        Assert.True(GpuSpecDb.Count >= 30);
    }

    [Fact]
    public void Subvendor_names_are_known_or_null()
    {
        Assert.Equal("ASUS", GpuSpecDb.SubvendorName("1043"));
        Assert.Equal("Galax / KFA2", GpuSpecDb.SubvendorName("1b4c"));
        Assert.Null(GpuSpecDb.SubvendorName("ABCD"));
    }

    [Fact]
    public void Parses_pci_ids_from_pnp_device_id()
    {
        var ids = GpuInfoCollector.ParsePnpId(@"PCI\VEN_10DE&DEV_2684&SUBSYS_88E21043&REV_A1\4&2B8260C3&0&0019");
        Assert.Equal(new GpuInfoCollector.PciIds("10DE", "2684", "88E2", "1043", "A1"), ids);

        var empty = GpuInfoCollector.ParsePnpId("ROOT\\BASICDISPLAY\\0000");
        Assert.Equal("", empty.Vendor);
    }

    [Theory]
    [InlineData("PCI bus 1, device 0, function 0", 1u)]
    [InlineData("PCI bus 10, device 0, function 0", 10u)]
    [InlineData("Barramento PCI 3, dispositivo 0", null)]
    [InlineData(null, null)]
    public void Reads_pci_bus_number(string? location, uint? expected)
        => Assert.Equal(expected, GpuInfoCollector.BusNumber(location));

    [Fact]
    public void Main_adapter_prefers_name_hint_then_most_vram_and_skips_software()
    {
        GpuAdapter A(int i, string name, ulong vramGb, bool sw = false)
            => new(i, name, 0x10DE, 0x2684, 0, 0, vramGb << 30, 0, 0, i, sw, sw ? null : "12_2");
        var list = new[] { A(0, "Intel UHD", 0), A(1, "NVIDIA RTX 4090", 24), A(2, "Microsoft Basic Render Driver", 0, sw: true) };

        Assert.Equal("NVIDIA RTX 4090", GpuAdapters.PickMain(list)!.Name);
        Assert.Equal("Intel UHD", GpuAdapters.PickMain(list, "uhd")!.Name);
        Assert.Null(GpuAdapters.PickMain(new[] { list[2] }));
    }

    [Fact]
    public void Nvml_and_dxgi_never_throw_without_hardware()
    {
        // No CI (sem NVIDIA) e fora do Windows, tudo deve degradar para "indisponível" sem exceção.
        _ = Nvml.Available;
        Assert.NotNull(Nvml.Devices());
        Assert.NotNull(GpuAdapters.Enumerate(includeSoftware: true));
    }
}

public class SceneMathTests
{
    [Fact]
    public void Average_fps_and_1_percent_low_follow_the_formulas()
    {
        // 99 frames de 10 ms + 1 de 40 ms: média = 100 / 1,03 s; 1% low = 1000 / p99.
        var times = Enumerable.Repeat(10.0, 99).Append(40.0).ToList();
        var r = GameSceneBench.BuildResult(times, 100, 1.03, 1920, 1080, "GPU");

        Assert.Equal(100 / 1.03, r.AvgFps, 3);
        Assert.Equal(1000 / 40.0, r.Low1Fps, 3);
        Assert.Equal(r.AvgFps * 10, r.Score, 6);

        var none = GameSceneBench.BuildResult(Array.Empty<double>(), 0, 0, 1, 1, "GPU");
        Assert.Equal(0, none.AvgFps);
        Assert.Equal(0, none.Low1Fps);
    }

    [Fact]
    public void Trees_are_deterministic_and_respect_terrain_rules()
    {
        var a = GameSceneBench.PlaceTrees(500);
        var b = GameSceneBench.PlaceTrees(500);
        Assert.Equal(a, b);
        Assert.All(a, t =>
        {
            float h = t.Y + 0.2f;
            Assert.InRange(h, 7.5f, 48f);           // acima d'água e abaixo da neve
            Assert.InRange(t.W, 0.7f, 1.6f);         // escala
            Assert.InRange(MathF.Abs(t.X), 0, 400f); // dentro do mapa
        });
    }

    [Fact]
    public void Gpu_result_conversion_keeps_comparable_metadata()
    {
        var scene = new GpuSceneResult(143.27, 98.5, 8600, 60.2, 1920, 1080, "NVIDIA GeForce RTX 4070");
        var preset = GpuBenchmark.PresetId(GpuBenchmark.ResolutionById("1080p"), 60);
        var r = GpuBenchmark.ToResult(scene, preset);

        Assert.Equal(TestIds.GpuScene, r.Test);
        Assert.Equal("1080p-60s", r.Preset);
        Assert.Equal(1433, r.Score);
        Assert.Equal("NVIDIA GeForce RTX 4070", r.DeviceName);
        Assert.Equal("1920x1080", r.Resolution);
        Assert.Equal(143.3, r.AvgFps);
        Assert.Equal(GameSceneBench.EngineVersion, r.EngineVersion);
        Assert.Equal("4k", GpuBenchmark.ResolutionById("4K").Id);
        Assert.Equal("1080p", GpuBenchmark.ResolutionById("nope").Id);
    }
}

[SupportedOSPlatform("windows")]
public class Direct3DTests
{
    [WindowsFact]
    public void Stress_shaders_compile()
    {
        Assert.False(Compiler.Compile(FurStress.Hlsl, "VS", "fur.hlsl", "vs_5_0", ShaderFlags.OptimizationLevel3).IsEmpty);
        Assert.False(Compiler.Compile(FurStress.Hlsl, "PS", "fur.hlsl", "ps_5_0", ShaderFlags.OptimizationLevel3).IsEmpty);
    }

    [WindowsFact]
    public void All_scene_shaders_compile()
    {
        foreach (var e in GameSceneBench.VertexEntries) Assert.False(GameSceneBench.Compile(e, "vs_5_0").IsEmpty, e);
        foreach (var e in GameSceneBench.PixelEntries) Assert.False(GameSceneBench.Compile(e, "ps_5_0").IsEmpty, e);
    }

    [WindowsFact]
    public void Stress_renders_frames_on_warp()
    {
        var stress = new FurStress();
        stress.Start(new FurStressSettings
        {
            Width = 320, Height = 180, Msaa = 4, Shells = 16, Iterations = 4,
            UseWarp = true, Headless = true, Duration = TimeSpan.FromSeconds(2),
        });
        Assert.True(stress.Join(TimeSpan.FromSeconds(60)), "o stress não terminou");
        Assert.Null(stress.Error);
        Assert.True(stress.Frames > 0, "nenhum frame renderizado");
        Assert.True(stress.AverageFps > 0);
        Assert.Contains("Microsoft", stress.AdapterUsed);
    }

    [WindowsFact]
    public void Game_scene_produces_a_result_on_warp()
    {
        var bench = new GameSceneBench
        {
            Width = 320, Height = 180, Duration = TimeSpan.FromSeconds(3), Warmup = TimeSpan.FromSeconds(0.5),
            UseWarp = true, Headless = true, TreeCount = 400, TerrainResolution = 64, ShadowResolution = 512,
        };
        var result = bench.Run();

        Assert.True(result != null, bench.Error ?? "sem resultado");
        Assert.True(result!.Frames > 0);
        Assert.True(result.AvgFps > 0);
        Assert.True(result.Low1Fps > 0 && result.Low1Fps <= result.AvgFps * 1.0001 + 1e-9 || result.Frames < 3);
        Assert.Equal(1, bench.Progress);
    }
}
