using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Localization;
using SupernovaBench.Graphics;

namespace SupernovaBench.App.ViewModels;

/// <summary>Leituras da GPU mostradas durante os testes (NVML; sem NVIDIA, cai para o LibreHardwareMonitor).</summary>
public sealed partial class GpuLive : ObservableObject
{
    [ObservableProperty] private string _temperature = "—";
    [ObservableProperty] private string _load = "—";
    [ObservableProperty] private string _clock = "—";
    [ObservableProperty] private string _power = "—";
    [ObservableProperty] private string _limiter = "—";

    public void Update(GpuSample? s)
    {
        var c = Loc.Culture;
        string F(double? v, string fmt) => v is { } x ? string.Format(c, fmt, x) : "—";
        if (s != null)
        {
            Temperature = F(s.TemperatureC, "{0:0} °C");
            Load = F(s.LoadPercent, "{0:0} %");
            Clock = F(s.CoreClockMHz, "{0:0} MHz");
            Power = s.PowerW is { } w
                ? (s.PowerLimitW is { } lim && lim > 0 ? string.Format(c, "{0:0} W · {1:0} %", w, 100 * w / lim) : string.Format(c, "{0:0} W", w))
                : "—";
            Limiter = s.ThrottleReasons is { } r ? GpuInfo.PerfCapText(r) : "—";
        }
        else
        {
            var sensors = AppServices.Sensors;
            Temperature = sensors.GpuTempText;
            Load = sensors.GpuLoadText;
            Clock = "—";
            Power = "—";
            Limiter = "—";
        }
    }
}
