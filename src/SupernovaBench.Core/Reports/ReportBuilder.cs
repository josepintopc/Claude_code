using System.Net;
using System.Text;
using System.Text.Json;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.Core.Reports;

/// <summary>
/// Gera relatórios HTML (com a identidade visual do Supernova Bench) e JSON (para importar em outro PC).
/// Os dois omitem os dados marcados como sensíveis (números de série, nome do computador).
/// </summary>
public static class ReportBuilder
{
    public static SystemReport Redact(SystemReport report) => new()
    {
        Format = report.Format,
        FormatVersion = report.FormatVersion,
        App = report.App,
        AppVersion = report.AppVersion,
        GeneratedAt = report.GeneratedAt,
        Language = report.Language,
        CpuName = report.CpuName,
        Sections = report.Sections.Select(s => s.Redacted()).ToList(),
        Results = report.Results.ToList(),
        Sensors = report.Sensors.ToList(),
    };

    public static string ToJson(SystemReport report)
        => JsonSerializer.Serialize(Redact(report), JsonFile.Options);

    /// <summary>
    /// Lê os resultados de um relatório JSON exportado por outro PC, marcando-os como importados.
    /// Lança <see cref="InvalidDataException"/> se o arquivo não for um relatório do Supernova Bench.
    /// </summary>
    public static IReadOnlyList<BenchmarkResult> ParseResultsForImport(string json, string? sourceLabel = null)
    {
        SystemReport? report;
        try { report = JsonSerializer.Deserialize<SystemReport>(json, JsonFile.Options); }
        catch (JsonException ex) { throw new InvalidDataException(ex.Message, ex); }

        if (report == null || report.Format != "supernova-bench-report")
            throw new InvalidDataException("not a Supernova Bench report");

        string label = sourceLabel ?? (string.IsNullOrWhiteSpace(report.CpuName) ? "?" : report.CpuName);
        return report.Results
                     .Where(r => !string.IsNullOrEmpty(r.Test) && r.Score > 0 && double.IsFinite(r.Score))
                     .Select(r => r with { Imported = true, Source = label })
                     .ToList();
    }

    public static string ToHtml(SystemReport source)
    {
        var report = Redact(source);
        var culture = Loc.Culture;
        var sb = new StringBuilder();
        string E(string? s) => WebUtility.HtmlEncode(s ?? "");

        sb.Append("<!doctype html><html lang=\"").Append(E(Loc.Current)).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(E(AppInfo.Name)).Append(" — ").Append(E(Loc.T("report.title"))).Append("</title>");
        sb.Append("<style>").Append(Css).Append("</style></head><body><main>");

        sb.Append("<header>").Append(LogoSvg).Append("<div><h1>Supernova <span>Bench</span></h1><p>")
          .Append(E(Loc.T("report.generatedAt", report.GeneratedAt.ToString("g", culture))))
          .Append(" · v").Append(E(report.AppVersion)).Append("</p></div></header>");

        // Resultados
        sb.Append("<section><h2>").Append(E(Loc.T("report.results"))).Append("</h2>");
        if (report.Results.Count == 0)
        {
            sb.Append("<p class=\"muted\">").Append(E(Loc.T("report.noResults"))).Append("</p>");
        }
        else
        {
            sb.Append("<table><thead><tr><th>").Append(E(Loc.T("report.col.test"))).Append("</th><th>")
              .Append(E(Loc.T("report.col.score"))).Append("</th><th>").Append(E(Loc.T("report.col.device"))).Append("</th><th>")
              .Append(E(Loc.T("report.col.details"))).Append("</th><th>").Append(E(Loc.T("report.col.time"))).Append("</th><th>")
              .Append(E(Loc.T("report.col.date"))).Append("</th></tr></thead><tbody>");
            foreach (var r in report.Results.OrderBy(r => r.Test).ThenByDescending(r => r.Score))
            {
                sb.Append("<tr><td>").Append(E(Loc.T(TestIds.NameKey(r.Test))));
                if (r.Imported) sb.Append(" <em>(").Append(E(Loc.T("report.imported"))).Append(")</em>");
                sb.Append("</td><td class=\"score\">").Append(E(r.Score.ToString("N0", culture))).Append(' ').Append(E(r.Unit))
                  .Append("</td><td>").Append(E(r.DeviceName)).Append("</td><td>").Append(E(Details(r, culture)))
                  .Append("</td><td>").Append(E(r.Seconds.ToString("N1", culture))).Append(" s</td><td>")
                  .Append(E(r.Timestamp.ToString("g", culture))).Append("</td></tr>");
            }
            sb.Append("</tbody></table>");
        }
        sb.Append("</section>");

        // Informações do sistema
        foreach (var section in report.Sections)
        {
            sb.Append("<section><h2>").Append(E(section.Title)).Append("</h2>");
            foreach (var node in section.Nodes) AppendNode(sb, node, E);
            sb.Append("</section>");
        }

        // Sensores
        if (report.Sensors.Count > 0)
        {
            sb.Append("<section><h2>").Append(E(Loc.T("report.sensors"))).Append("</h2><table><thead><tr><th>")
              .Append(E(Loc.T("sensors.col.hardware"))).Append("</th><th>").Append(E(Loc.T("sensors.col.sensor"))).Append("</th><th>")
              .Append(E(Loc.T("sensors.col.type"))).Append("</th><th>").Append(E(Loc.T("sensors.col.value"))).Append("</th><th>")
              .Append(E(Loc.T("sensors.col.min"))).Append("</th><th>").Append(E(Loc.T("sensors.col.max"))).Append("</th></tr></thead><tbody>");
            foreach (var s in report.Sensors)
                sb.Append("<tr><td>").Append(E(s.Hardware)).Append("</td><td>").Append(E(s.Name)).Append("</td><td>")
                  .Append(E(s.Category)).Append("</td><td>").Append(E(s.Value)).Append("</td><td>").Append(E(s.Min))
                  .Append("</td><td>").Append(E(s.Max)).Append("</td></tr>");
            sb.Append("</tbody></table></section>");
        }

        sb.Append("<footer>").Append(E(Loc.T("report.privacy"))).Append("</footer></main></body></html>");
        return sb.ToString();
    }

    /// <summary>Detalhes do resultado: threads (CPU), resolução e FPS (GPU), banda e latência (memória) ou velocidades (disco).</summary>
    public static string Details(BenchmarkResult r, System.Globalization.CultureInfo culture)
    {
        if (r.AvgFps is { } avg)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(r.Resolution)) parts.Add(r.Resolution);
            parts.Add(Loc.T("report.fps", avg.ToString("N0", culture)));
            if (r.Low1Fps is { } low) parts.Add(Loc.T("report.low1", low.ToString("N0", culture)));
            return string.Join(" · ", parts);
        }
        if (r.Test == TestIds.MemoryBench && r.Metric("mem.read") is { } read)
            return Loc.T("report.memory", read.ToString("N1", culture), (r.Metric("mem.write") ?? 0).ToString("N1", culture),
                         (r.Metric("mem.latency") ?? 0).ToString("N1", culture));
        if (r.Test == TestIds.DiskBench && r.Metric("seq1m_q8t1.read") is { } seqRead)
            return Loc.T("report.disk", seqRead.ToString("N0", culture), (r.Metric("seq1m_q8t1.write") ?? 0).ToString("N0", culture),
                         (r.Metric("rnd4k_q1t1.read") ?? 0).ToString("N1", culture));
        return Loc.T("report.threads", r.Threads);
    }

    private static void AppendNode(StringBuilder sb, InfoNode node, Func<string?, string> e)
    {
        if (node.IsGroup)
        {
            sb.Append("<div class=\"group\"><h3>").Append(e(node.Label));
            if (node.HasValue) sb.Append(" <span>").Append(e(node.Value)).Append("</span>");
            sb.Append("</h3><dl>");
            foreach (var c in node.Children)
            {
                if (c.IsGroup) { sb.Append("</dl>"); AppendNode(sb, c, e); sb.Append("<dl>"); continue; }
                sb.Append("<dt>").Append(e(c.Label)).Append("</dt><dd>").Append(e(c.Value)).Append("</dd>");
            }
            sb.Append("</dl></div>");
        }
        else
        {
            sb.Append("<dl><dt>").Append(e(node.Label)).Append("</dt><dd>").Append(e(node.Value)).Append("</dd></dl>");
        }
    }

    public const string LogoSvg =
        "<svg class=\"logo\" viewBox=\"0 0 64 64\" aria-hidden=\"true\"><defs><radialGradient id=\"g\" cx=\"50%\" cy=\"50%\" r=\"50%\">" +
        "<stop offset=\"0\" stop-color=\"#FFF4E6\"/><stop offset=\".35\" stop-color=\"#FF7A2F\"/><stop offset=\".7\" stop-color=\"#E0379B\"/>" +
        "<stop offset=\"1\" stop-color=\"#7B3FF2\" stop-opacity=\"0\"/></radialGradient></defs><circle cx=\"32\" cy=\"32\" r=\"30\" fill=\"url(#g)\"/>" +
        "<path d=\"M32 4 L36 28 L60 32 L36 36 L32 60 L28 36 L4 32 L28 28 Z\" fill=\"#FFF4E6\"/></svg>";

    private const string Css =
        ":root{--bg:#0B0A14;--panel:#13111F;--card:#1A1729;--line:#2A2540;--fg:#F2EEFF;--muted:#9A93B8;--a1:#FF7A2F;--a2:#E0379B;--a3:#7B3FF2}" +
        "*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--fg);font:15px/1.5 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif}" +
        "main{max-width:1080px;margin:0 auto;padding:32px 16px 48px}" +
        "header{display:flex;align-items:center;gap:16px;padding:20px 24px;border-radius:20px;background:linear-gradient(120deg,rgba(255,122,47,.18),rgba(224,55,155,.14),rgba(123,63,242,.18));border:1px solid var(--line)}" +
        ".logo{width:56px;height:56px;flex:none}h1{margin:0;font-size:28px;letter-spacing:.5px}h1 span{font-weight:300}header p{margin:2px 0 0;color:var(--muted)}" +
        "section{margin-top:20px;padding:20px 24px;border-radius:16px;background:var(--panel);border:1px solid var(--line);overflow-x:auto}" +
        "h2{margin:0 0 12px;font-size:18px;background:linear-gradient(90deg,var(--a1),var(--a2),var(--a3));-webkit-background-clip:text;background-clip:text;color:transparent}" +
        "h3{margin:12px 0 6px;font-size:15px}h3 span{color:var(--muted);font-weight:400}" +
        ".group{padding:8px 14px;margin:8px 0;border-radius:12px;background:var(--card)}" +
        "dl{display:grid;grid-template-columns:minmax(160px,34%) 1fr;gap:4px 16px;margin:4px 0}dt{color:var(--muted)}dd{margin:0;word-break:break-word}" +
        "table{width:100%;border-collapse:collapse}th,td{text-align:left;padding:6px 10px;border-bottom:1px solid var(--line)}th{color:var(--muted);font-weight:600}" +
        "td.score{font-weight:700;color:var(--a1);font-family:'Cascadia Mono',Consolas,monospace}em{color:var(--muted)}.muted{color:var(--muted)}" +
        "footer{margin-top:24px;color:var(--muted);font-size:13px;text-align:center}" +
        "@media (prefers-color-scheme:light){:root{--bg:#F6F4FB;--panel:#FFFFFF;--card:#F1EEF8;--line:#E2DCF0;--fg:#1A1530;--muted:#6B6488}}";
}
