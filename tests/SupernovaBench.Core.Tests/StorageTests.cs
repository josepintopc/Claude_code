using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Storage;

namespace SupernovaBench.Core.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "supernova-tests-" + Guid.NewGuid().ToString("N"));

    public StorageTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static BenchmarkResult R(string test, double score, string preset = "standard", int minutesAgo = 0) => new()
    {
        Test = test, Score = score, Preset = preset, EngineVersion = "1.0", CpuName = "Test CPU",
        Timestamp = DateTimeOffset.Now.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void History_round_trips_through_disk()
    {
        var path = Path.Combine(_dir, "history.json");
        var h = new ResultHistory(path);
        var a = R(TestIds.CpuMulti, 1234.5);
        h.Add(a);
        h.Add(R(TestIds.CpuSingle, 210, minutesAgo: 5));

        var reloaded = new ResultHistory(path);
        Assert.Equal(2, reloaded.All.Count);
        var back = reloaded.All.Single(r => r.Id == a.Id);
        Assert.Equal(a, back);
    }

    [Fact]
    public void Ranking_only_includes_comparable_results_best_first()
    {
        var h = new ResultHistory(Path.Combine(_dir, "h.json"));
        h.AddRange(new[]
        {
            R(TestIds.CpuMulti, 100), R(TestIds.CpuMulti, 300), R(TestIds.CpuMulti, 200),
            R(TestIds.CpuMulti, 999, preset: "quick"), R(TestIds.CpuSingle, 50),
        });

        var ranking = h.Ranking(TestIds.CpuMulti, "standard", "1.0");
        Assert.Equal(new[] { 300.0, 200.0, 100.0 }, ranking.Select(r => r.Score));
    }

    [Fact]
    public void Duplicate_ids_are_ignored_and_remove_and_clear_work()
    {
        var h = new ResultHistory(Path.Combine(_dir, "h.json"));
        var a = R(TestIds.CpuMulti, 1);
        Assert.Equal(1, h.AddRange(new[] { a, a }));
        Assert.Equal(0, h.AddRange(new[] { a }));
        Assert.True(h.Remove(a.Id));
        Assert.Empty(h.All);
        h.Add(R(TestIds.CpuMulti, 2));
        h.Clear();
        Assert.Empty(new ResultHistory(Path.Combine(_dir, "h.json")).All);
    }

    [Fact]
    public void Corrupt_files_do_not_crash_and_are_backed_up()
    {
        var path = Path.Combine(_dir, "history.json");
        File.WriteAllText(path, "{ isto não é json");
        var h = new ResultHistory(path);
        Assert.Empty(h.All);
        Assert.True(File.Exists(path + ".corrupt"));

        var settingsPath = Path.Combine(_dir, "settings.json");
        File.WriteAllText(settingsPath, "[]");
        var s = AppSettings.Load(settingsPath);
        Assert.Equal("pt-BR", s.Language);
    }

    [Fact]
    public void Settings_round_trip_and_clamp()
    {
        var path = Path.Combine(_dir, "settings.json");
        new AppSettings { Language = "en", Theme = "light", SensorIntervalMs = 5 }.Save(path);
        var s = AppSettings.Load(path);
        Assert.Equal("en", s.Language);
        Assert.Equal("light", s.Theme);
        Assert.Equal(250, s.SensorIntervalMs);
    }
}
