using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Extras;

namespace SupernovaBench.Core.Tests;

public class FrameStatsTests
{
    [Fact]
    public void Fps_and_frame_time_from_presents()
    {
        var c = new FrameCounter();
        for (int i = 0; i <= 120; i++) c.AddPresent(100, i * 8.0);     // 125 FPS
        for (int i = 0; i <= 30; i++) c.AddPresent(200, i * 33.3);     // ~30 FPS
        var s = c.Snapshot(100, 960)!;
        Assert.Equal(125, s.Fps, 0);
        Assert.Equal(8.0, s.FrameTimeMs, 3);
        Assert.Equal(100, c.Busiest(960));
        Assert.Null(c.Snapshot(300, 960));
        Assert.Null(c.Snapshot(100, 5000));       // parou de entregar quadros
        c.Prune(5000);
        Assert.Null(c.Busiest(5000));
    }

    [Fact]
    public void Session_average_and_one_percent_low()
    {
        // 990 quadros de 10 ms e 10 travadas de 50 ms.
        var times = Enumerable.Repeat(10.0, 990).Concat(Enumerable.Repeat(50.0, 10)).ToList();
        var s = FrameSession.FromFrameTimes(1, times);
        Assert.Equal(1000, s.Frames);
        Assert.Equal(1000.0 * 1000 / (990 * 10 + 10 * 50), s.AverageFps, 3);
        Assert.Equal(20, s.Low1Fps, 3);   // percentil 99 = 50 ms

        var c = new FrameCounter();
        c.StartSession(7);
        for (int i = 0; i < 100; i++) c.AddPresent(7, i * 16.0);
        c.AddPresent(8, 5);                // outro processo não entra
        c.AddPresent(7, 99 * 16.0 + 10_000); // pausa longa (alt+tab) não conta
        var rec = c.StopSession()!;
        Assert.Equal(99, rec.Frames);
        Assert.Equal(62.5, rec.AverageFps, 3);
        Assert.Null(c.CurrentSession);
    }
}

public class BottleneckTests
{
    private static PerfSample S(double? gpu, double? cpu = 40, double? core = 60, double? vram = 50, double? ram = 50, double? fps = 100, int? hz = 144)
        => new(gpu, cpu, core, vram, ram, fps, hz);

    [Fact]
    public void Classifies_the_common_situations()
    {
        Assert.Equal(Bottleneck.Gpu, BottleneckAnalyzer.Classify(S(99)));
        Assert.Equal(Bottleneck.Cpu, BottleneckAnalyzer.Classify(S(60, cpu: 92, core: 98)));
        Assert.Equal(Bottleneck.CpuSingleThread, BottleneckAnalyzer.Classify(S(55, cpu: 35, core: 97)));
        Assert.Equal(Bottleneck.Vram, BottleneckAnalyzer.Classify(S(70, vram: 99)));
        Assert.Equal(Bottleneck.Ram, BottleneckAnalyzer.Classify(S(70, ram: 95)));
        Assert.Equal(Bottleneck.FpsCap, BottleneckAnalyzer.Classify(S(50, fps: 143.6)));
        Assert.Equal(Bottleneck.FpsCap, BottleneckAnalyzer.Classify(S(40, fps: 60.2, hz: 60)));
        Assert.Equal(Bottleneck.Balanced, BottleneckAnalyzer.Classify(S(90)));
        Assert.Equal(Bottleneck.Unknown, BottleneckAnalyzer.Classify(S(null)));
        Assert.Equal(Bottleneck.Unknown, BottleneckAnalyzer.Classify(S(50, fps: 87)));
        Assert.Equal("bottleneck.cpusinglethread", BottleneckAnalyzer.Key(Bottleneck.CpuSingleThread));
    }

    [Fact]
    public void Window_smooths_spikes_and_session_shares_add_up()
    {
        var a = new BottleneckAnalyzer(windowSeconds: 3);
        for (int i = 0; i < 6; i++) a.Add(S(99));
        // Um segundo isolado com a GPU baixa não muda o veredito (média da janela ainda ≥ 95? não: 99,99,60 → 86 → equilibrado).
        Assert.Equal(Bottleneck.Balanced, a.Add(S(60)));
        for (int i = 0; i < 3; i++) a.Add(S(50, cpu: 90, core: 99));
        Assert.Equal(Bottleneck.Gpu, a.Dominant);
        Assert.Equal(1.0, a.Shares().Values.Sum(), 6);
        a.Reset();
        Assert.Equal(0, a.Samples);
        Assert.Equal(Bottleneck.Unknown, a.Dominant);
    }
}

public class CommunityTests
{
    private static BenchmarkResult R(double score, string cpu = "AMD Ryzen 7 7800X3D", string preset = "standard") => new()
    {
        Test = TestIds.CpuMulti, Score = score, CpuName = cpu, Preset = preset, EngineVersion = "1.0", AppVersion = "0.1.0", Threads = 16, Seconds = 30,
    };

    [Fact]
    public void Parses_and_rejects_invalid_entries()
    {
        string json = """
            { "format": "supernova-community", "version": 1, "results": [
              { "id": "6f1d2a51-1111-4c4e-9e5a-000000000001", "test": "cpu.multi", "score": 30123.4, "cpuName": "AMD Ryzen 9 7950X", "preset": "standard", "engineVersion": "1.0", "threads": 32, "seconds": 25 },
              { "id": "6f1d2a51-1111-4c4e-9e5a-000000000002", "test": "cpu.hack", "score": 1, "cpuName": "x", "preset": "standard", "engineVersion": "1.0" },
              { "id": "6f1d2a51-1111-4c4e-9e5a-000000000003", "test": "cpu.multi", "score": -5, "cpuName": "x", "preset": "standard", "engineVersion": "1.0" },
              { "id": "6f1d2a51-1111-4c4e-9e5a-000000000004", "test": "cpu.multi", "score": 99, "cpuName": "", "preset": "standard", "engineVersion": "1.0" } ] }
            """;
        var list = CommunityResults.Parse(json);
        var r = Assert.Single(list);
        Assert.Equal("AMD Ryzen 9 7950X", r.CpuName);
        Assert.True(r.Imported);
        Assert.Throws<InvalidDataException>(() => CommunityResults.Parse("""{ "format": "outro", "results": [] }"""));
        Assert.False(CommunityResults.IsValid(R(double.NaN)));
        Assert.False(CommunityResults.IsValid(R(100, cpu: new string('x', 200))));
        Assert.False(CommunityResults.IsValid(R(100, cpu: "CPU\nstatus=accepted")));
        Assert.False(CommunityResults.IsValid(R(100, cpu: "<img src=x>")));
        Assert.False(CommunityResults.IsValid(R(100) with { Metrics = new() { ["a b"] = 1 } }));
        Assert.True(CommunityResults.IsValid(R(100) with { Metrics = new() { ["seq1m_q8t1.read"] = 1 } }));
        Assert.Equal("https://raw.githubusercontent.com/josepintopc/Claude_code/HEAD/community/results.json",
                     CommunityResults.RawUrl("https://github.com/josepintopc/Claude_code"));
    }

    [Fact]
    public void Ranking_puts_my_result_in_place_and_computes_percentile()
    {
        var community = Enumerable.Range(1, 99).Select(i => R(i * 100, cpu: $"CPU {i}")).ToList();
        var mine = R(5050, cpu: "Meu PC");
        var rank = CommunityResults.Rank(community, mine, top: 10);
        Assert.Equal(100, rank.Total);
        Assert.Equal(50, rank.MyPosition);
        Assert.Equal(50, rank.TopPercent);
        Assert.Equal(11, rank.Rows.Count);          // top 10 + a minha linha
        Assert.True(rank.Rows[^1].IsMine);
        Assert.Equal(9900, rank.Rows[0].Result.Score);

        var none = CommunityResults.Rank(community, null);
        Assert.Null(none.MyPosition);
        Assert.Null(none.TopPercent);
    }

    [Fact]
    public void Issue_url_round_trips_and_carries_only_public_fields()
    {
        var mine = R(28765.4) with { Source = @"C:\Users\Fulano\relatorio.json", Imported = true };
        string url = CommunityResults.IssueUrl("https://github.com/josepintopc/Claude_code", mine, "CPU · Multi-core");
        Assert.StartsWith("https://github.com/josepintopc/Claude_code/issues/new?title=%5Bresultado%5D%20CPU", url);
        Assert.DoesNotContain("Fulano", Uri.UnescapeDataString(url));

        string body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        var back = CommunityResults.ParseIssueBody(body)!;
        Assert.Equal(mine.Id, back.Id);
        Assert.Equal(28765.4, back.Score);
        Assert.False(back.Imported);
        Assert.Null(back.Source);
        Assert.Null(CommunityResults.ParseIssueBody("sem bloco"));
        Assert.Null(CommunityResults.ParseIssueBody("```json\n{ \"test\": \"cpu.multi\", \"score\": 1e99 }\n```"));
    }
}
