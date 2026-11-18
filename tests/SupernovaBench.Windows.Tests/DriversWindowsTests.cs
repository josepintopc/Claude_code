using SupernovaBench.Core.Drivers;
using SupernovaBench.Hardware.Drivers;

namespace SupernovaBench.Windows.Tests;

public class DriversWindowsTests
{
    [WindowsFact]
    public void Inventory_lists_drivers_with_names_and_versions()
    {
        var drivers = DriverScanner.Inventory();
        Assert.NotEmpty(drivers);
        Assert.All(drivers, d => Assert.False(string.IsNullOrWhiteSpace(d.DeviceName)));
        Assert.Contains(drivers, d => !string.IsNullOrWhiteSpace(d.Version));
        Assert.Contains(drivers, d => d.Date != null);
    }

    [WindowsFact]
    public void Problem_devices_and_restore_points_never_throw()
    {
        var problems = DriverScanner.ProblemDevices();
        Assert.All(problems, p => Assert.NotEqual(0, p.ErrorCode));
        var points = RestorePoints.List();
        Assert.All(points, p => Assert.NotNull(p.Description));
    }

    [WindowsFact]
    public void Windows_update_session_can_be_created()
    {
        Assert.True(WindowsUpdateDrivers.IsAvailable());
    }
}
