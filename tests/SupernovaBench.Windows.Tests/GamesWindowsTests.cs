using System.Diagnostics;
using SupernovaBench.Core.Games;
using SupernovaBench.Games;

namespace SupernovaBench.Windows.Tests;

public class GamesWindowsTests
{
    [WindowsFact]
    public void Scanning_every_store_never_throws()
    {
        var manual = new[] { new GameEntry { Id = "manual:x", Name = "X", Store = GameStore.Manual, InstallDir = @"C:\Nope", Executable = @"C:\Nope\x.exe" } };
        var r = GameScanner.ScanAll(manual);
        Assert.Contains(r.Games, g => g.Id == "manual:x");
        Assert.All(r.Games, g => Assert.False(string.IsNullOrWhiteSpace(g.Name)));
    }

    [WindowsFact]
    public void Process_snapshot_contains_this_process()
    {
        string self = Environment.ProcessPath!;
        Assert.Contains(ProcessWatcher.Snapshot(), p => string.Equals(p.Path, self, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(self, ProcessWatcher.ImagePath(Environment.ProcessId), StringComparer.OrdinalIgnoreCase);
    }

    [WindowsFact]
    public void Power_plans_are_listed_and_the_active_one_is_among_them()
    {
        var plans = PowerPlans.List();
        Assert.NotEmpty(plans);
        var active = PowerPlans.Active();
        Assert.NotNull(active);
        Assert.Contains(plans, p => p.Id == active);
        Assert.Null(PowerPlans.Resolve(PowerPlanChoice.Keep, plans.Select(p => p.Id).ToList()));
    }

    [WindowsFact]
    public void Gpu_preference_round_trip()
    {
        string exe = $@"C:\SupernovaBenchTest\{Guid.NewGuid():N}.exe";
        try
        {
            Assert.Equal(GpuChoice.Default, GpuPreference.Get(exe));
            GpuPreference.Set(exe, GpuChoice.HighPerformance);
            Assert.Equal(GpuChoice.HighPerformance, GpuPreference.Get(exe));
            GpuPreference.Set(exe, GpuChoice.PowerSaving);
            Assert.Equal(GpuChoice.PowerSaving, GpuPreference.Get(exe));
        }
        finally
        {
            GpuPreference.Set(exe, GpuChoice.Default);
        }
        Assert.Equal(GpuChoice.Default, GpuPreference.Get(exe));
        Assert.Equal(GpuChoice.HighPerformance, GpuPreference.Parse("SwapEffectUpgradeEnable=1;GpuPreference=2;"));
    }
}
