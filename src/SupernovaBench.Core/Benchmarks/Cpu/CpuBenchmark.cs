namespace SupernovaBench.Core.Benchmarks.Cpu;

public enum CpuBenchMode { MultiCore, SingleCore, Sustained }

/// <summary>Executa o <see cref="CpuRenderBench"/> num dos modos e transforma o resultado em <see cref="BenchmarkResult"/>.</summary>
public static class CpuBenchmark
{
    /// <summary>Tempo mínimo do modo sustentado (mesmo critério do Cinebench: 10 minutos).</summary>
    public static readonly TimeSpan SustainedDuration = TimeSpan.FromMinutes(10);

    public static string TestId(CpuBenchMode mode) => mode switch
    {
        CpuBenchMode.SingleCore => TestIds.CpuSingle,
        CpuBenchMode.Sustained => TestIds.CpuSustained,
        _ => TestIds.CpuMulti,
    };

    public static int ThreadsFor(CpuBenchMode mode) => mode == CpuBenchMode.SingleCore ? 1 : Environment.ProcessorCount;

    public static TimeSpan DurationFor(CpuBenchMode mode, TimeSpan? sustainedOverride = null)
        => mode == CpuBenchMode.Sustained ? sustainedOverride ?? SustainedDuration : TimeSpan.Zero;

    /// <summary>Roda o teste de forma síncrona (chame fora da thread da interface). Retorna null se cancelado.</summary>
    public static BenchmarkResult? Run(CpuRenderBench renderer, CpuBenchMode mode, CancellationToken ct,
                                       int? threads = null, TimeSpan? sustainedOverride = null, Action<int>? onPass = null)
    {
        int n = threads ?? ThreadsFor(mode);
        var stats = renderer.Run(n, DurationFor(mode, sustainedOverride), ct, onPass);
        if (stats.Cancelled || stats.Passes == 0) return null;
        return ToResult(stats, mode, renderer.Preset);
    }

    public static BenchmarkResult ToResult(RenderRunStats stats, CpuBenchMode mode, RenderPreset preset) => new()
    {
        Test = TestId(mode),
        Score = Math.Round(stats.Score, 1),
        Unit = "pts",
        CpuName = MachineInfo.CpuName(),
        Threads = stats.Threads,
        Seconds = Math.Round(stats.Seconds, 3),
        Samples = stats.Samples,
        Passes = stats.Passes,
        Preset = preset.Id,
        EngineVersion = CpuRenderBench.EngineVersion,
        AppVersion = AppInfo.Version,
    };
}
