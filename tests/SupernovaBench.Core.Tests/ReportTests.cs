using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Reports;

namespace SupernovaBench.Core.Tests;

public class ReportTests
{
    private static SystemReport Sample()
    {
        var board = new InfoSection { Id = "board", Title = "Placa-mãe" };
        var mb = new InfoNode { Label = "Placa-mãe" };
        mb.Add("Modelo", "ROG STRIX B650-A");
        mb.Add("Nº de série", "SERIAL-ABC-123", sensitive: true);
        board.Nodes.Add(mb);
        board.Nodes.Add(new InfoNode { Label = "Nome do computador", Value = "PC-DO-JOSE", Sensitive = true });

        return new SystemReport
        {
            CpuName = "AMD Ryzen 7 7700X",
            Sections = { board },
            Results =
            {
                new BenchmarkResult { Test = TestIds.CpuMulti, Score = 4321.5, CpuName = "AMD Ryzen 7 7700X", Preset = "standard", EngineVersion = "1.0", Threads = 16, Seconds = 12.3 },
            },
            Sensors = { new SensorSnapshot("CPU", "Temperatura", "Package", "61 °C", "40 °C", "88 °C") },
        };
    }

    [Fact]
    public void Exports_never_contain_sensitive_data()
    {
        var report = Sample();
        var html = ReportBuilder.ToHtml(report);
        var json = ReportBuilder.ToJson(report);

        foreach (var output in new[] { html, json })
        {
            Assert.DoesNotContain("SERIAL-ABC-123", output);
            Assert.DoesNotContain("PC-DO-JOSE", output);
            Assert.Contains("ROG STRIX B650-A", output);
        }
        // O relatório original não é alterado.
        Assert.Equal(2, report.Sections[0].Nodes.Count);
    }

    [Fact]
    public void Html_is_branded_and_escapes_content()
    {
        var report = Sample();
        report.Sections[0].Nodes.Add(new InfoNode { Label = "<script>alert(1)</script>", Value = "a & b" });
        var html = ReportBuilder.ToHtml(report);

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("Supernova", html);
        Assert.Contains("#FF7A2F", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("a &amp; b", html);
    }

    [Fact]
    public void Json_export_can_be_imported_as_comparison_results()
    {
        var json = ReportBuilder.ToJson(Sample());
        var imported = ReportBuilder.ParseResultsForImport(json);

        var r = Assert.Single(imported);
        Assert.True(r.Imported);
        Assert.Equal("AMD Ryzen 7 7700X", r.Source);
        Assert.Equal(4321.5, r.Score);
        Assert.Equal(TestIds.CpuMulti, r.Test);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"format\":\"something-else\",\"results\":[]}")]
    [InlineData("null")]
    public void Import_rejects_files_that_are_not_reports(string json)
    {
        Assert.Throws<InvalidDataException>(() => ReportBuilder.ParseResultsForImport(json));
    }

    [Fact]
    public void Plain_text_copy_respects_privacy_flag()
    {
        var section = Sample().Sections[0];
        Assert.Contains("SERIAL-ABC-123", section.ToText(includeSensitive: true));
        Assert.DoesNotContain("SERIAL-ABC-123", section.ToText(includeSensitive: false));
    }
}
