using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Info;

namespace SupernovaBench.Core.Reports;

/// <summary>Linha de sensor congelada no momento da exportação.</summary>
public sealed record SensorSnapshot(string Hardware, string Category, string Name, string Value, string Min, string Max);

/// <summary>Conteúdo de um relatório exportado (HTML ou JSON). Também é o formato importado de outros PCs.</summary>
public sealed class SystemReport
{
    public string Format { get; set; } = "supernova-bench-report";
    public int FormatVersion { get; set; } = 1;
    public string App { get; set; } = AppInfo.Name;
    public string AppVersion { get; set; } = AppInfo.Version;
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.Now;
    public string Language { get; set; } = "";
    public string CpuName { get; set; } = "";
    public List<InfoSection> Sections { get; set; } = new();
    public List<BenchmarkResult> Results { get; set; } = new();
    public List<SensorSnapshot> Sensors { get; set; } = new();
}
