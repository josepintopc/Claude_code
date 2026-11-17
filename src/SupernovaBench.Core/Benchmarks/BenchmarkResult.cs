namespace SupernovaBench.Core.Benchmarks;

/// <summary>Identificadores dos testes (gravados no histórico e nos relatórios; não traduzir).</summary>
public static class TestIds
{
    public const string CpuMulti = "cpu.multi";
    public const string CpuSingle = "cpu.single";
    public const string CpuSustained = "cpu.sustained";

    public static readonly IReadOnlyList<string> All = new[] { CpuMulti, CpuSingle, CpuSustained };

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
