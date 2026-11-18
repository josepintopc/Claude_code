using System.Text;
using System.Text.Json;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.Core.Extras;

/// <summary>Arquivo community/results.json do repositório (resultados enviados pela comunidade).</summary>
public sealed class CommunityFile
{
    public string Format { get; set; } = CommunityResults.Format;
    public int Version { get; set; } = 1;
    public DateTimeOffset? Updated { get; set; }
    public List<BenchmarkResult> Results { get; set; } = new();
}

public sealed record CommunityRow(int Position, BenchmarkResult Result, bool IsMine);

public sealed record CommunityRanking(IReadOnlyList<CommunityRow> Rows, int? MyPosition, int Total)
{
    /// <summary>Percentil do meu resultado (top X%), quando ele está no ranking.</summary>
    public double? TopPercent => MyPosition is { } p && Total > 0 ? 100.0 * p / Total : null;
}

/// <summary>
/// Comparação online sem servidor: a lista da comunidade é um arquivo JSON no repositório público (lido por HTTPS)
/// e o envio é uma issue do GitHub já preenchida, validada e incorporada por uma GitHub Action.
/// </summary>
public static class CommunityResults
{
    public const string Format = "supernova-community";
    public const string IssueTitlePrefix = "[resultado]";

    public static string RawUrl(string repositoryUrl) => repositoryUrl.Replace("https://github.com/", "https://raw.githubusercontent.com/") + "/HEAD/community/results.json";

    public static List<BenchmarkResult> Parse(string json)
    {
        var file = JsonSerializer.Deserialize<CommunityFile>(json, JsonFile.Options);
        if (file == null || file.Format != Format) throw new InvalidDataException("not a Supernova community file");
        return file.Results.Where(IsValid).Select(r => r with { Imported = true, Source = "community" }).ToList();
    }

    /// <summary>Mesmas regras da GitHub Action (.github/scripts/accept_result.py).</summary>
    public static bool IsValid(BenchmarkResult r)
    {
        bool ShortText(string? s, bool required) => s == null
            ? !required
            : (!required || s.Trim().Length > 0) && s.Length <= 120 && !s.Any(c => c < ' ' || c is '<' or '>' or '`' or '|');
        bool MetricKey(string k) => k.Length is > 0 and <= 40 && k.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.');
        return TestIds.All.Contains(r.Test)
               && double.IsFinite(r.Score) && r.Score > 0 && r.Score < 10_000_000
               && ShortText(r.CpuName, true) && ShortText(r.GpuName, false) && ShortText(r.Device, false)
               && ShortText(r.Preset, true) && ShortText(r.EngineVersion, true) && ShortText(r.AppVersion, false)
               && r.Threads is >= 0 and <= 4096 && double.IsFinite(r.Seconds) && r.Seconds >= 0
               && (r.Metrics == null || (r.Metrics.Count <= 64 && r.Metrics.All(kv => MetricKey(kv.Key) && double.IsFinite(kv.Value))));
    }

    /// <summary>Só os campos públicos do resultado (nada de nome do PC, número de série ou caminho de arquivo).</summary>
    public static BenchmarkResult Sanitize(BenchmarkResult r) => new()
    {
        Id = r.Id,
        Test = r.Test,
        Score = Math.Round(r.Score, 1),
        Unit = r.Unit,
        Timestamp = r.Timestamp,
        CpuName = r.CpuName,
        GpuName = r.GpuName,
        AvgFps = r.AvgFps,
        Low1Fps = r.Low1Fps,
        Resolution = r.Resolution,
        Device = r.Device,
        Metrics = r.Metrics,
        Threads = r.Threads,
        Seconds = r.Seconds,
        Samples = r.Samples,
        Passes = r.Passes,
        Preset = r.Preset,
        EngineVersion = r.EngineVersion,
        AppVersion = r.AppVersion,
    };

    /// <summary>Ranking do teste/preset/motor: comunidade + o melhor resultado deste PC (destacado).</summary>
    public static CommunityRanking Rank(IEnumerable<BenchmarkResult> community, BenchmarkResult? mine, int top = 50)
    {
        var all = community.Where(r => mine == null || r.Id != mine.Id).ToList();
        if (mine != null) all.Add(mine);
        var ordered = all.OrderByDescending(r => r.Score).ToList();
        int? myPos = mine == null ? null : ordered.FindIndex(r => r.Id == mine.Id) + 1;
        var rows = ordered.Select((r, i) => new CommunityRow(i + 1, r, mine != null && r.Id == mine.Id)).Take(top).ToList();
        if (myPos is > 0 && myPos > top) rows.Add(new CommunityRow(myPos.Value, mine!, true));
        return new CommunityRanking(rows, myPos, ordered.Count);
    }

    /// <summary>Endereço que abre no navegador uma issue já preenchida com o resultado (o usuário só confirma).</summary>
    public static string IssueUrl(string repositoryUrl, BenchmarkResult result, string testName)
    {
        var clean = Sanitize(result);
        string json = JsonSerializer.Serialize(clean, JsonFile.Options);
        string title = $"{IssueTitlePrefix} {testName} · {clean.DeviceName} · {clean.Score:0}";
        var body = new StringBuilder()
            .AppendLine("Resultado enviado pelo Supernova Bench. Não edite o bloco abaixo: ele é validado e adicionado à comparação online automaticamente.")
            .AppendLine()
            .AppendLine("```json")
            .AppendLine(json)
            .AppendLine("```");
        return $"{repositoryUrl}/issues/new?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body.ToString())}";
    }

    /// <summary>Lê o bloco JSON de uma issue (usado nos testes, em paridade com o script Python da Action).</summary>
    public static BenchmarkResult? ParseIssueBody(string body)
    {
        int start = body.IndexOf("```json", StringComparison.Ordinal);
        if (start < 0) return null;
        start = body.IndexOf('\n', start) + 1;
        int end = body.IndexOf("```", start, StringComparison.Ordinal);
        if (start <= 0 || end < 0) return null;
        try
        {
            var r = JsonSerializer.Deserialize<BenchmarkResult>(body[start..end], JsonFile.Options);
            return r != null && IsValid(r) ? Sanitize(r) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
