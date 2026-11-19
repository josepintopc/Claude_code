using System.Globalization;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Reports;

namespace SupernovaBench.Core.Tests;

public class GpuResultTests
{
    private static BenchmarkResult Gpu() => new()
    {
        Test = TestIds.GpuScene, Score = 1433, CpuName = "AMD Ryzen 7 7700X", GpuName = "NVIDIA GeForce RTX 4070",
        AvgFps = 143.3, Low1Fps = 98.6, Resolution = "1920x1080", Preset = "1080p-60s", EngineVersion = "1.0",
    };

    [Fact]
    public void Device_name_is_the_gpu_for_gpu_tests_and_the_cpu_otherwise()
    {
        Assert.Equal("NVIDIA GeForce RTX 4070", Gpu().DeviceName);
        Assert.Equal("AMD Ryzen 7 7700X", (Gpu() with { GpuName = null }).DeviceName);
        Assert.Equal("AMD Ryzen 7 7700X", new BenchmarkResult { Test = TestIds.CpuMulti, CpuName = "AMD Ryzen 7 7700X", GpuName = "x" }.DeviceName);
        Assert.True(TestIds.IsGpu(TestIds.GpuScene));
        Assert.False(TestIds.IsGpu(TestIds.CpuMulti));
    }

    [Fact]
    public void Report_details_show_fps_for_gpu_and_threads_for_cpu()
    {
        try
        {
            Loc.SetLanguage("en");
            var inv = CultureInfo.GetCultureInfo("en");
            Assert.Equal("1920x1080 · 143 avg FPS · 99 FPS 1% low", ReportBuilder.Details(Gpu(), inv));
            Assert.Equal("16 threads", ReportBuilder.Details(new BenchmarkResult { Test = TestIds.CpuMulti, Threads = 16 }, inv));
            Assert.Equal("1 thread", ReportBuilder.Details(new BenchmarkResult { Test = TestIds.CpuSingle, Threads = 1 }, inv));
        }
        finally
        {
            Loc.SetLanguage("pt-BR");
        }
    }

    [Fact]
    public void Gpu_results_round_trip_through_report_import()
    {
        var report = new SystemReport { CpuName = "AMD Ryzen 7 7700X", Results = { Gpu() } };
        var html = ReportBuilder.ToHtml(report);
        Assert.Contains("NVIDIA GeForce RTX 4070", html);

        var imported = Assert.Single(ReportBuilder.ParseResultsForImport(ReportBuilder.ToJson(report)));
        Assert.Equal(143.3, imported.AvgFps);
        Assert.Equal("1080p-60s", imported.Preset);
        Assert.Equal("NVIDIA GeForce RTX 4070", imported.DeviceName);
    }
}
