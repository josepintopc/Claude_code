using LibreHardwareMonitor.Hardware;
using SupernovaBench.Hardware;

namespace SupernovaBench.Windows.Tests;

/// <summary>Regras sobre os nomes e valores que o LibreHardwareMonitor entrega (casos vistos em PCs reais).</summary>
public class SensorMonitorTests
{
    [Theory]
    [InlineData("Core #1")]          // Intel antiga
    [InlineData("CPU Core #12")]     // cargas e clocks atuais
    [InlineData("CPU Core #1 Thread #2")]
    [InlineData("P-Core #1")]        // Intel híbrida (Core Ultra 9 275HX)
    [InlineData("E-Core #16")]
    public void Core_sensor_names_are_recognized(string name) => Assert.True(SensorMonitor.IsCoreSensorName(name));

    [Theory]
    [InlineData("CPU Total")]
    [InlineData("CPU Core Max")]
    [InlineData("Core Max")]
    [InlineData("Core Average")]
    [InlineData("Bus Speed")]
    [InlineData("CPU Package")]
    public void Other_cpu_sensors_are_not_cores(string name) => Assert.False(SensorMonitor.IsCoreSensorName(name));

    [Fact]
    public void Hardware_names_lose_nul_padding_and_spaces()
    {
        Assert.Equal("", SensorMonitor.CleanName(new string('\0', 40))); // SK hynix HFS001TEJ9X125N
        Assert.Equal("", SensorMonitor.CleanName(new string(' ', 40)));
        Assert.Equal("", SensorMonitor.CleanName(null));
        Assert.Equal("Samsung SSD 990 PRO 2TB", SensorMonitor.CleanName("Samsung SSD 990 PRO 2TB\0\0\0  "));
        Assert.Equal("Intel Core Ultra 9 275HX", SensorMonitor.CleanName("Intel Core Ultra 9 275HX"));
    }

    [Fact]
    public void Zero_degrees_is_no_reading_except_distance_to_tjmax()
    {
        Assert.Null(SensorMonitor.Plausible(SensorType.Temperature, "Composite Temperature", 0));
        Assert.Null(SensorMonitor.Plausible(SensorType.Temperature, "Warning Temperature", -273));
        Assert.Equal(69f, SensorMonitor.Plausible(SensorType.Temperature, "Warning Temperature", 69));
        Assert.Equal(0f, SensorMonitor.Plausible(SensorType.Temperature, "P-Core #1 Distance to TjMax", 0));
        Assert.Equal(0f, SensorMonitor.Plausible(SensorType.Load, "CPU Core #6", 0));
        Assert.Null(SensorMonitor.Plausible(SensorType.Temperature, "CPU Package", null));
    }

    [Fact]
    public void Broken_nvme_health_log_is_detected()
    {
        // O que a biblioteca devolveu para um SK hynix HFS001TEJ9X125N.
        var broken = new (SensorType, string, float?)[]
        {
            (SensorType.Temperature, "Composite Temperature", 0),
            (SensorType.Temperature, "Warning Temperature", 69),
            (SensorType.Temperature, "Critical Temperature", 74),
            (SensorType.Level, "Life", 101),
            (SensorType.Level, "Available Spare", 0),
            (SensorType.Level, "Available Spare Threshold", 255),
            (SensorType.Level, "Percentage Used", 255),
        };
        Assert.True(SensorMonitor.NvmeHealthLooksInvalid(broken));

        var healthy = new (SensorType, string, float?)[]
        {
            (SensorType.Temperature, "Composite Temperature", 41),
            (SensorType.Level, "Available Spare", 100),
            (SensorType.Level, "Available Spare Threshold", 10),
            (SensorType.Level, "Percentage Used", 2),
        };
        Assert.False(SensorMonitor.NvmeHealthLooksInvalid(healthy));

        // SATA (sem os sensores NVMe) nunca é marcado.
        Assert.False(SensorMonitor.NvmeHealthLooksInvalid(new (SensorType, string, float?)[] { (SensorType.Temperature, "Temperature", 35) }));
    }

    [Theory]
    [InlineData(SensorType.Load, "Used Space", true)]
    [InlineData(SensorType.Load, "Total Activity", true)]
    [InlineData(SensorType.Throughput, "Read Rate", true)]
    [InlineData(SensorType.Data, "Free Space", true)]
    [InlineData(SensorType.Data, "Total Space", true)]
    [InlineData(SensorType.Temperature, "Warning Temperature", true)]
    [InlineData(SensorType.Temperature, "Critical Temperature", true)]
    [InlineData(SensorType.Temperature, "Composite Temperature", false)]
    [InlineData(SensorType.Level, "Available Spare", false)]
    [InlineData(SensorType.Level, "Percentage Used", false)]
    [InlineData(SensorType.Level, "Life", false)]
    [InlineData(SensorType.Data, "Data Read", false)]
    [InlineData(SensorType.Factor, "Power On Hours", false)]
    public void Only_windows_side_disk_sensors_survive_a_broken_health_log(SensorType type, string name, bool keep)
        => Assert.Equal(keep, SensorMonitor.KeepWhenHealthLogInvalid(type, name));
}
