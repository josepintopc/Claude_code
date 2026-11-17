namespace SupernovaBench.Core.Benchmarks;

/// <summary>Identificadores dos testes (gravados no histórico e nos relatórios; não traduzir).</summary>
public static class TestIds
{
    public const string CpuMulti = "cpu.multi";
    public const string CpuSingle = "cpu.single";
    public const string CpuSustained = "cpu.sustained";
    public const string GpuScene = "gpu.scene";

    public static readonly IReadOnlyList<string> All = new[] { CpuMulti, CpuSingle, CpuSustained, GpuScene };

    public static bool IsGpu(string testId) => testId.StartsWith("gpu.", StringComparison.Ordinal);

    /// <summary>Chave de tradução com o nome amigável do teste.</summary>
    public static string NameKey(string testId) => "test." + testId;
}

/// <summary>Um resultado de benchmark. Não guarda nome do computador nem números de série (privacidade).</summary>
public sealed record BenchmarkResult
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Test { get; init; } = "";
    public double Score { get; init; }
    public string Unit { get; init; } = "pts";
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    public string CpuName { get; init; } = "";

    /// <summary>Placa de vídeo usada (testes de GPU).</summary>
    public string? GpuName { get; init; }

    /// <summary>FPS médio e "1% low" (testes de GPU).</summary>
    public double? AvgFps { get; init; }
    public double? Low1Fps { get; init; }

    /// <summary>Resolução de renderização (testes de GPU), ex.: "1920x1080".</summary>
    public string? Resolution { get; init; }

    /// <summary>Hardware avaliado pelo teste: a GPU nos testes de GPU, a CPU nos de CPU.</summary>
    public string DeviceName => TestIds.IsGpu(Test) && !string.IsNullOrWhiteSpace(GpuName) ? GpuName! : CpuName;
    public int Threads { get; init; }
    public double Seconds { get; init; }
    public long Samples { get; init; }
    public int Passes { get; init; }
    public string Preset { get; init; } = "";
    public string EngineVersion { get; init; } = "";
    public string AppVersion { get; init; } = "";

    /// <summary>Verdadeiro quando o resultado veio de um relatório de outro PC (importado para comparação).</summary>
    public bool Imported { get; init; }

    /// <summary>Rótulo livre da origem (ex.: nome do arquivo importado).</summary>
    public string? Source { get; init; }

    /// <summary>Dois resultados só são comparáveis se forem do mesmo teste, preset e versão do motor.</summary>
    public bool IsComparableWith(BenchmarkResult other)
        => Test == other.Test && Preset == other.Preset && EngineVersion == other.EngineVersion;
}
