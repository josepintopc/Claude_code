using SupernovaBench.Core;
using SupernovaBench.Core.Benchmarks;

namespace SupernovaBench.Graphics;

/// <summary>Presets do benchmark de GPU e conversão do resultado para o histórico.</summary>
public static class GpuBenchmark
{
    public sealed record Resolution(string Id, int Width, int Height);

    public static readonly IReadOnlyList<Resolution> Resolutions = new[]
    {
        new Resolution("1080p", 1920, 1080),
        new Resolution("1440p", 2560, 1440),
        new Resolution("4k", 3840, 2160),
    };

    public static readonly IReadOnlyList<int> DurationsSeconds = new[] { 30, 60 };

    public static Resolution ResolutionById(string? id)
        => Resolutions.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Resolutions[0];

    /// <summary>Preset gravado no resultado: resolução + duração (só resultados iguais entram no mesmo ranking).</summary>
    public static string PresetId(Resolution res, int seconds) => $"{res.Id}-{seconds}s";

    public static BenchmarkResult ToResult(GpuSceneResult r, string preset) => new()
    {
        Test = TestIds.GpuScene,
        Score = Math.Round(r.Score, 0),
        Unit = "pts",
        GpuName = r.Adapter,
        CpuName = MachineInfo.CpuName(),
        AvgFps = Math.Round(r.AvgFps, 1),
        Low1Fps = Math.Round(r.Low1Fps, 1),
        Resolution = $"{r.Width}x{r.Height}",
        Seconds = Math.Round(r.Seconds, 2),
        Samples = r.Frames,
        Passes = 1,
        Preset = preset,
        EngineVersion = GameSceneBench.EngineVersion,
        AppVersion = AppInfo.Version,
    };
}
