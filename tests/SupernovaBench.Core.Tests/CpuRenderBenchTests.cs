using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Cpu;

namespace SupernovaBench.Core.Tests;

public class CpuRenderBenchTests
{
    private static readonly RenderPreset Tiny = new("tiny", 96, 54, 8);

    [Fact]
    public void Image_is_deterministic_regardless_of_thread_count()
    {
        var single = new CpuRenderBench(Tiny);
        single.Run(1, TimeSpan.Zero, CancellationToken.None);

        var multi = new CpuRenderBench(Tiny);
        multi.Run(4, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(single.ImageHash(), multi.ImageHash());
    }

    [Fact]
    public void Renders_every_sample_and_every_bucket()
    {
        var r = new CpuRenderBench(Tiny);
        var stats = r.Run(2, TimeSpan.Zero, CancellationToken.None);

        Assert.Equal(Tiny.TotalSamples, stats.Samples);
        Assert.Equal(r.Buckets.Count, r.BucketsDone);
        Assert.Equal(1, stats.Passes);
        Assert.False(stats.Cancelled);
        Assert.All(r.Active, a => Assert.Equal(-1, a));
    }

    [Fact]
    public void Image_is_not_black_and_has_color()
    {
        var r = new CpuRenderBench(Tiny);
        r.Run(2, TimeSpan.Zero, CancellationToken.None);

        int colored = r.Pixels.Count(p =>
        {
            int red = (p >> 16) & 0xFF, green = (p >> 8) & 0xFF, blue = p & 0xFF;
            return Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 24;
        });
        Assert.True(r.Pixels.Average(p => (p >> 8) & 0xFF) > 10, "imagem escura demais");
        Assert.True(colored > r.Pixels.Length / 10, "imagem sem cor");
    }

    [Fact]
    public void Buckets_cover_the_image_exactly_once()
    {
        var r = new CpuRenderBench(new RenderPreset("odd", 100, 70, 1));
        var covered = new int[100 * 70];
        foreach (var b in r.Buckets)
            for (int y = b.Y; y < b.Y + b.H; y++)
                for (int x = b.X; x < b.X + b.W; x++)
                    covered[y * 100 + x]++;
        Assert.All(covered, c => Assert.Equal(1, c));
        // O primeiro bloco é o do centro (como no Cinebench).
        var first = r.Buckets[0];
        Assert.InRange(50, first.X - CpuRenderBench.BucketSize, first.X + first.W + CpuRenderBench.BucketSize);
    }

    [Fact]
    public void Cancellation_stops_the_run()
    {
        var r = new CpuRenderBench(RenderPreset.Standard);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var stats = r.Run(2, TimeSpan.Zero, cts.Token);

        Assert.True(stats.Cancelled);
        Assert.Equal(0, stats.Passes);
        Assert.Null(CpuBenchmark.Run(new CpuRenderBench(Tiny), CpuBenchMode.MultiCore, cts.Token));
    }

    [Fact]
    public void Sustained_mode_repeats_passes_until_min_duration()
    {
        var r = new CpuRenderBench(Tiny);
        var result = CpuBenchmark.Run(r, CpuBenchMode.Sustained, CancellationToken.None, threads: 2,
                                      sustainedOverride: TimeSpan.FromMilliseconds(300));

        Assert.NotNull(result);
        Assert.Equal(TestIds.CpuSustained, result!.Test);
        Assert.True(result.Passes >= 2, $"passadas: {result.Passes}");
        Assert.True(result.Seconds >= 0.3);
        Assert.Equal(Tiny.TotalSamples * result.Passes, result.Samples);
    }

    [Theory]
    [InlineData(10_000_000L, 1.0, 1000.0)]
    [InlineData(5_000_000L, 2.0, 250.0)]
    [InlineData(123L, 0.0, 0.0)]
    public void Score_is_samples_per_second_over_ten_thousand(long samples, double seconds, double expected)
    {
        Assert.Equal(expected, CpuRenderBench.ComputeScore(samples, seconds), 6);
    }

    [Fact]
    public void Result_carries_metadata_for_comparison()
    {
        var result = CpuBenchmark.Run(new CpuRenderBench(Tiny), CpuBenchMode.SingleCore, CancellationToken.None)!;

        Assert.Equal(TestIds.CpuSingle, result.Test);
        Assert.Equal(1, result.Threads);
        Assert.Equal("tiny", result.Preset);
        Assert.Equal(CpuRenderBench.EngineVersion, result.EngineVersion);
        Assert.False(string.IsNullOrWhiteSpace(result.CpuName));
        Assert.True(result.Score > 0);
    }
}
