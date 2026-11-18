using SupernovaBench.Core.Info;
using SupernovaBench.Hardware;

namespace SupernovaBench.Windows.Tests;

public class DiskInfoTests
{
    [WindowsFact]
    public void Drives_list_the_system_drive()
    {
        var drives = DiskInfo.Drives();
        var system = Path.GetPathRoot(Environment.SystemDirectory)!.TrimEnd('\\');
        Assert.Contains(drives, d => string.Equals(d.Letter, system, StringComparison.OrdinalIgnoreCase));
        Assert.All(drives, d => Assert.True(d.TotalBytes > 0));
    }

    [WindowsFact]
    public void Health_reading_never_throws_and_gives_a_verdict_for_each_disk()
    {
        var disks = DiskInfo.ReadHealth();
        Assert.All(disks, d =>
        {
            Assert.True(Enum.IsDefined(d.Status));
            Assert.False(string.IsNullOrEmpty(DiskInfo.StatusText(d)));
        });
    }

    [WindowsFact]
    public void Nvme_query_on_a_missing_disk_returns_null()
    {
        Assert.Null(DiskInfo.ReadNvmeLog(250));
    }
}
