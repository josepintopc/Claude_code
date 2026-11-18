using System.Text.Json;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Benchmarks.Cpu;
using SupernovaBench.Core.Benchmarks.Disk;
using SupernovaBench.Core.Benchmarks.Memory;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.Core.Tests;

public class MemoryBenchTests
{
    [Fact]
    public void Quick_run_measures_every_level_and_operation()
    {
        var bench = new MemoryBench(quick: true, threads: 2);
        int measured = 0;
        bench.Measured = (_, _, _) => measured++;
        var r = bench.Run(CancellationToken.None);

        Assert.NotNull(r);
        Assert.Equal(16, measured);
        foreach (var level in MemoryBench.Levels)
            foreach (var op in MemoryBench.Ops)
                Assert.True(r!.Get(level, op) > 0, $"{level} {op}");

        // A latência do L1 é muito menor que a da RAM.
        Assert.True(r!.Get(MemLevel.L1, MemOp.Latency) < r.Get(MemLevel.Memory, MemOp.Latency));
        Assert.True(r.Score > 0);
        // O modo rápido tem 16 medidas curtas: se o tempo explodir, alguma thread ficou sem CPU (bug visto no Windows).
        Assert.True(r.Seconds < 30, $"levou {r.Seconds:0.0} s");
        Assert.True(r.Get(MemLevel.Memory, MemOp.Copy) < 2000, "banda de cópia irreal");

        var result = bench.ToResult(r, "16 GB DDR4");
        Assert.Equal(TestIds.MemoryBench, result.Test);
        Assert.Equal("16 GB DDR4", result.DeviceName);
        Assert.Equal(r.Get(MemLevel.Memory, MemOp.Read), result.Metric("mem.read"));
        Assert.Equal("quick", result.Preset);
    }

    [Fact]
    public void Reference_machine_scores_1000()
    {
        Assert.Equal(1000, MemoryBench.ComputeScore(MemoryBench.RefReadGBs, MemoryBench.RefWriteGBs, MemoryBench.RefCopyGBs, MemoryBench.RefLatencyNs));
        // O dobro de banda em tudo e metade da latência: 2×.
        Assert.Equal(2000, MemoryBench.ComputeScore(90, 90, 80, 40));
        Assert.Equal(0, MemoryBench.ComputeScore(0, 1, 1, 1));
    }

    [Fact]
    public void Cancelled_run_returns_null()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Null(new MemoryBench(quick: true, threads: 1).Run(cts.Token));
    }

    [Fact]
    public void Latency_chase_order_visits_every_line_once_block_by_block()
    {
        var order = MemoryBench.BuildChaseOrder(10_000, 1024);
        Assert.Equal(Enumerable.Range(0, 10_000), order.OrderBy(i => i));
        // Cada bloco de 1024 linhas é percorrido inteiro antes de passar ao próximo: só 9 trocas de bloco em 10 blocos.
        int changes = 0;
        for (int i = 1; i < order.Length; i++)
            if (order[i] / 1024 != order[i - 1] / 1024) changes++;
        Assert.Equal(9, changes);
        Assert.NotEqual(Enumerable.Range(0, 10_000), order);
    }

    [Fact]
    public void Cache_sizes_are_built_from_the_raw_entries()
    {
        var c = CacheInfo.Build(new List<(int, int, long)>
        {
            (1, 2, 48 * 1024), (1, 1, 32 * 1024), (1, 2, 32 * 1024), (2, 0, 2 << 20), (2, 0, 1 << 20), (3, 0, 32L << 20), (3, 0, 32L << 20),
        }, 8)!;
        Assert.Equal(32 * 1024, c.L1);
        Assert.Equal(1 << 20, c.L2);
        Assert.Equal(32L << 20, c.L3);
        Assert.Equal(64L << 20, c.L3Total);
        Assert.Equal(8, c.Cores);
        Assert.Null(CacheInfo.Build(new List<(int, int, long)>(), 4));
        Assert.Equal(48 * 1024, CacheInfo.ParseSize("48K"));
        Assert.Equal(32L << 20, CacheInfo.ParseSize("32768K"));
        Assert.True(CacheInfo.Detect().L1 > 0);
    }
}

public class DiskBenchTests
{
    [Fact]
    public async Task Runs_the_eight_tests_on_a_temp_folder_and_removes_the_file()
    {
        var dir = Directory.CreateTempSubdirectory("supernova-disk-").FullName;
        try
        {
            var bench = new DiskBench(new DiskBenchOptions
            {
                Directory = dir, FileSize = 16L << 20, Runs = 1, TestDuration = TimeSpan.FromMilliseconds(150), Interval = TimeSpan.Zero,
            });
            double prepared = 0;
            bench.Preparing = p => prepared = p;
            var r = await bench.RunAsync(CancellationToken.None);

            Assert.NotNull(r);
            Assert.Equal(1.0, prepared, 3);
            foreach (var t in DiskBench.Tests)
            {
                Assert.True(r!.Read[t.Id].MBps > 0, t.Id);
                Assert.True(r.Write[t.Id].MBps > 0, t.Id);
                Assert.True(r.Read[t.Id].Iops > 0);
                Assert.True(r.Read[t.Id].LatencyUs > 0);
            }
            Assert.False(File.Exists(Path.Combine(dir, DiskBench.FileName)));

            var result = bench.ToResult(r!, "Samsung SSD 990 PRO 2TB");
            Assert.Equal(TestIds.DiskBench, result.Test);
            Assert.Equal("16m-0s", result.Preset);
            Assert.Equal(Math.Round(r!.Read["seq1m_q8t1"].MBps, 2), result.Metric("seq1m_q8t1.read"));
            Assert.NotNull(result.Metric("rnd4k_q1t1.write.iops"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Reference_ssd_scores_1000()
    {
        var r = new DiskBenchResult();
        foreach (var (id, (rd, wr)) in DiskBench.Reference)
        {
            r.Read[id] = new DiskMeasure(rd, 1, 1);
            r.Write[id] = new DiskMeasure(wr, 1, 1);
        }
        Assert.Equal(1000, r.Score);
        r.Write.Remove("rnd4k_q1t1");
        Assert.Equal(0, r.Score);
        Assert.Equal("1g-5s", DiskBench.PresetId(1L << 30, TimeSpan.FromSeconds(5)));
        Assert.Equal("256m-5s", DiskBench.PresetId(256L << 20, TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task Missing_folder_is_reported()
    {
        var bench = new DiskBench(new DiskBenchOptions { Directory = Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()) });
        var ex = await Assert.ThrowsAsync<DiskBenchException>(() => bench.RunAsync(CancellationToken.None));
        Assert.Equal("access", ex.Key);
    }
}

public class SmartTests
{
    private static byte[] AtaData(params (byte Id, byte Cur, byte Worst, long Raw)[] attrs)
    {
        var d = new byte[512];
        for (int i = 0; i < attrs.Length; i++)
        {
            int o = 2 + i * 12;
            d[o] = attrs[i].Id;
            d[o + 1] = 0x32;
            d[o + 3] = attrs[i].Cur;
            d[o + 4] = attrs[i].Worst;
            for (int b = 0; b < 6; b++) d[o + 5 + b] = (byte)(attrs[i].Raw >> (8 * b));
        }
        return d;
    }

    private static byte[] Thresholds(params (byte Id, byte Limit)[] t)
    {
        var d = new byte[512];
        for (int i = 0; i < t.Length; i++)
        {
            d[2 + i * 12] = t[i].Id;
            d[2 + i * 12 + 1] = t[i].Limit;
        }
        return d;
    }

    [Fact]
    public void Ata_attributes_are_parsed_and_a_healthy_ssd_is_good()
    {
        var attrs = SmartParser.ParseAta(
            AtaData((5, 100, 100, 0), (9, 99, 99, 12345 | (7L << 32)), (12, 99, 99, 321), (177, 97, 97, 40), (194, 67, 52, 33 | (55L << 16))),
            Thresholds((5, 10), (177, 5)));

        Assert.Equal(5, attrs.Count);
        var hours = attrs.Single(a => a.Id == 9);
        Assert.Equal(12345, hours.DisplayRaw);
        Assert.Equal(33, attrs.Single(a => a.Id == 194).DisplayRaw);
        Assert.Equal(10, attrs.Single(a => a.Id == 5).Threshold);
        Assert.Equal("smart.attr.9", hours.NameKey);
        Assert.Equal("smart.attr.unknown", SmartParser.AttributeNameKey(250));

        var d = new DiskHealth { Model = "Samsung SSD 870 EVO" };
        SmartParser.Apply(d, attrs, predictFailure: false);
        d.Evaluate();
        Assert.Equal(HealthStatus.Good, d.Status);
        Assert.Equal(97, d.LifePercent);
        Assert.Equal(33, d.TemperatureC);
        Assert.Equal(12345, d.PowerOnHours);
        Assert.Equal(321, d.PowerCycles);
        Assert.Empty(d.Notes);
    }

    [Fact]
    public void Reallocated_sectors_give_caution_and_a_failing_attribute_gives_bad()
    {
        var caution = new DiskHealth();
        SmartParser.Apply(caution, SmartParser.ParseAta(AtaData((5, 100, 100, 8), (197, 100, 100, 2))), null);
        caution.Evaluate();
        Assert.Equal(HealthStatus.Caution, caution.Status);
        Assert.Contains(caution.Notes, n => n.Key == "health.note.reallocated" && n.Value == 8);
        Assert.Contains(caution.Notes, n => n.Key == "health.note.pending" && n.Value == 2);

        var bad = new DiskHealth();
        SmartParser.Apply(bad, SmartParser.ParseAta(AtaData((5, 5, 5, 4000)), Thresholds((5, 10))), null);
        bad.Evaluate();
        Assert.Equal(HealthStatus.Bad, bad.Status);

        var predicted = new DiskHealth { PredictFailure = true }.Evaluate();
        Assert.Equal(HealthStatus.Bad, predicted.Status);
        Assert.Equal(HealthStatus.Unknown, new DiskHealth().Evaluate().Status);
    }

    [Fact]
    public void Nvme_health_log_is_parsed()
    {
        var log = new byte[512];
        log[0] = 0;
        BitConverter.GetBytes((ushort)(273 + 41)).CopyTo(log, 1);
        log[3] = 100;
        log[4] = 10;
        log[5] = 3;
        BitConverter.GetBytes(2_000_000UL).CopyTo(log, 32);   // lidos
        BitConverter.GetBytes(1_000_000UL).CopyTo(log, 48);   // gravados
        BitConverter.GetBytes(812UL).CopyTo(log, 112);
        BitConverter.GetBytes(4321UL).CopyTo(log, 128);
        BitConverter.GetBytes(37UL).CopyTo(log, 144);
        BitConverter.GetBytes(1UL).CopyTo(log, 136);          // parte alta das horas: satura

        var n = SmartParser.ParseNvme(log);
        Assert.Equal(41, n.TemperatureC);
        Assert.Equal(3, n.PercentUsed);
        Assert.Equal(512e9, n.DataWrittenBytes);
        Assert.Equal(1.024e12, n.DataReadBytes);
        Assert.Equal(812, n.PowerCycles);
        Assert.Equal(long.MaxValue, n.PowerOnHours);
        Assert.Equal(37, n.UnsafeShutdowns);

        var d = new DiskHealth();
        SmartParser.Apply(d, n with { PowerOnHours = 4321 });
        d.Evaluate();
        Assert.Equal(HealthStatus.Good, d.Status);
        Assert.Equal(97, d.LifePercent);
        Assert.Equal("nvme", d.Source);

        var warn = new DiskHealth();
        SmartParser.Apply(warn, n with { CriticalWarning = 0x04 });
        Assert.Equal(HealthStatus.Bad, warn.Evaluate().Status);

        var worn = new DiskHealth();
        SmartParser.Apply(worn, n with { PercentUsed = 95 });
        Assert.Equal(HealthStatus.Caution, worn.Evaluate().Status);
        Assert.Equal(5, worn.LifePercent);
    }
}

public class StressTests
{
    [Theory]
    [InlineData(CpuStressMode.Fma)]
    [InlineData(CpuStressMode.Integer)]
    public void Cpu_stress_runs_without_errors_and_stops(CpuStressMode mode)
    {
        var s = new CpuStress();
        s.Start(mode, 2, TimeSpan.FromMilliseconds(300));
        s.Join();
        Assert.False(s.Running);
        Assert.True(s.Iterations > 0);
        Assert.Equal(0, s.Errors);
        Assert.Null(s.Error);
    }

    [Theory]
    [InlineData(CpuStressMode.Fma)]
    [InlineData(CpuStressMode.Integer)]
    [InlineData(CpuStressMode.Render)]
    public void Cpu_stress_detects_a_calculation_error(CpuStressMode mode)
    {
        var s = new CpuStress { InjectFaultAfter = 1, StopOnError = true };
        s.Start(mode, 2, TimeSpan.FromSeconds(60));
        s.Join();
        Assert.True(s.Errors >= 1);
        Assert.NotNull(s.FirstErrorThread);
        Assert.True(s.Elapsed < TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void Calculation_blocks_are_deterministic()
    {
        Assert.Equal(CpuStress.FmaBlock(0), CpuStress.FmaBlock(0));
        Assert.Equal(CpuStress.IntBlock(0), CpuStress.IntBlock(0));
        Assert.NotEqual(CpuStress.IntBlock(0), CpuStress.IntBlock(1));
    }

    [Fact]
    public void Ram_stress_completes_one_pass_without_errors()
    {
        var s = new RamStress();
        s.Start(16L << 20, 2, maxPasses: 1);
        s.Join();
        Assert.Null(s.Error);
        Assert.Equal(0, s.ErrorCount);
        Assert.True(s.Coverage >= 99.9, $"coverage {s.Coverage}");
        Assert.Equal(1, s.Passes);
        Assert.True(s.MBps > 0);
    }

    [Fact]
    public void Ram_stress_finds_an_injected_fault_at_the_right_offset()
    {
        long offset = (8L << 20) + 4096 + 24; // na área da segunda thread
        var s = new RamStress { InjectFaultAt = offset };
        s.Start(16L << 20, 2, maxPasses: 1);
        s.Join();
        Assert.Equal(1, s.ErrorCount);
        var e = Assert.Single(s.Errors);
        Assert.Equal(offset, e.Offset);
        Assert.Equal("address", e.Pattern);
        Assert.Equal(1, e.BitsFlipped);
    }

    [Fact]
    public void Ram_stress_stops_on_request()
    {
        var s = new RamStress();
        s.Start(64L << 20, 2);
        Thread.Sleep(100);
        s.Stop();
        Assert.False(s.Running);
        Assert.Equal(0, s.ErrorCount);
    }
}

public class ResultModelTests
{
    [Fact]
    public void Device_and_metrics_survive_the_history_file_and_old_files_still_load()
    {
        var dir = Directory.CreateTempSubdirectory("supernova-hist-").FullName;
        try
        {
            var path = Path.Combine(dir, "history.json");
            var h = new ResultHistory(path);
            h.Add(new BenchmarkResult
            {
                Test = TestIds.DiskBench, Score = 4200, CpuName = "CPU", Device = "WD Black SN850X 1TB",
                Metrics = new() { ["seq1m_q8t1.read"] = 7100.5 }, Preset = "1g-5s", EngineVersion = DiskBench.EngineVersion,
            });
            var back = Assert.Single(new ResultHistory(path).All);
            Assert.Equal("WD Black SN850X 1TB", back.DeviceName);
            Assert.Equal(7100.5, back.Metric("seq1m_q8t1.read"));
            Assert.Null(back.Metric("nope"));

            // Histórico antigo, sem os campos novos.
            File.WriteAllText(path, """{"results":[{"test":"cpu.multi","score":12000,"cpuName":"Old CPU"}]}""");
            var old = Assert.Single(new ResultHistory(path).All);
            Assert.Equal("Old CPU", old.DeviceName);
            Assert.Null(old.Metrics);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
