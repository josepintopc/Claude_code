namespace SupernovaBench.Core.Extras;

public enum Bottleneck { Unknown, Gpu, Cpu, CpuSingleThread, Vram, Ram, FpsCap, Balanced }

/// <summary>Leitura de um segundo durante o jogo (o que estiver disponível; null = sem sensor).</summary>
public sealed record PerfSample(double? GpuLoad, double? CpuLoad, double? MaxCoreLoad, double? VramLoad, double? RamLoad, double? Fps, int? RefreshHz);

/// <summary>
/// Detector de gargalo: olha a carga da GPU, da CPU (total e do núcleo mais carregado), VRAM, RAM e o FPS.
/// Regras simples e explicáveis, avaliadas sobre a média dos últimos segundos para não oscilar.
/// </summary>
public sealed class BottleneckAnalyzer
{
    private readonly int _window;
    private readonly Queue<PerfSample> _recent = new();
    private readonly Dictionary<Bottleneck, int> _counts = new();

    public BottleneckAnalyzer(int windowSeconds = 5) => _window = Math.Max(1, windowSeconds);

    public int Samples => _counts.Values.Sum();

    /// <summary>Adiciona uma leitura e devolve o veredito atual (média da janela).</summary>
    public Bottleneck Add(PerfSample sample)
    {
        _recent.Enqueue(sample);
        while (_recent.Count > _window) _recent.Dequeue();
        var verdict = Classify(Average(_recent.ToList()));
        if (verdict != Bottleneck.Unknown) _counts[verdict] = _counts.GetValueOrDefault(verdict) + 1;
        return verdict;
    }

    /// <summary>Parte do tempo (0..1) em cada situação, desde o início.</summary>
    public IReadOnlyDictionary<Bottleneck, double> Shares()
    {
        double total = Samples;
        return _counts.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => total > 0 ? kv.Value / total : 0);
    }

    /// <summary>Situação mais frequente da sessão.</summary>
    public Bottleneck Dominant => _counts.Count == 0 ? Bottleneck.Unknown : _counts.OrderByDescending(kv => kv.Value).First().Key;

    public void Reset()
    {
        _recent.Clear();
        _counts.Clear();
    }

    private static readonly int[] CommonCaps = { 30, 60, 75, 90, 100, 120, 144, 165, 170, 180, 200, 240, 360 };

    public static Bottleneck Classify(PerfSample s)
    {
        if (s.GpuLoad is not { } gpu) return Bottleneck.Unknown;
        if (s.VramLoad is >= 97) return Bottleneck.Vram;
        if (s.RamLoad is >= 92) return Bottleneck.Ram;
        if (gpu >= 95) return Bottleneck.Gpu;

        bool cpuBusy = s.CpuLoad is >= 85;
        bool coreBusy = s.MaxCoreLoad is >= 90;
        if (gpu < 85 && cpuBusy) return Bottleneck.Cpu;
        if (gpu < 85 && coreBusy) return Bottleneck.CpuSingleThread;

        if (gpu < 85 && s.Fps is { } fps && fps > 0 && IsNearCap(fps, s.RefreshHz)) return Bottleneck.FpsCap;
        if (gpu >= 85) return Bottleneck.Balanced;
        return Bottleneck.Unknown;
    }

    /// <summary>FPS travado num valor redondo (V-Sync, limitador do jogo ou do driver).</summary>
    public static bool IsNearCap(double fps, int? refreshHz)
    {
        var caps = refreshHz is > 0 ? CommonCaps.Append(refreshHz.Value) : CommonCaps;
        return caps.Any(c => Math.Abs(fps - c) <= Math.Max(1.5, c * 0.02));
    }

    private static PerfSample Average(List<PerfSample> list)
    {
        double? Avg(Func<PerfSample, double?> f)
        {
            var v = list.Select(f).Where(x => x != null).Select(x => x!.Value).ToList();
            return v.Count > 0 ? v.Average() : null;
        }
        return new PerfSample(Avg(s => s.GpuLoad), Avg(s => s.CpuLoad), Avg(s => s.MaxCoreLoad), Avg(s => s.VramLoad), Avg(s => s.RamLoad),
                              Avg(s => s.Fps), list.LastOrDefault()?.RefreshHz);
    }

    public static string Key(Bottleneck b) => "bottleneck." + b.ToString().ToLowerInvariant();
}
