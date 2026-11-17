using SupernovaBench.Graphics.Native;

namespace SupernovaBench.Graphics;

/// <summary>Leitura instantânea da GPU principal pela NVML (só NVIDIA).</summary>
public sealed record GpuSample(double? TemperatureC, double? LoadPercent, double? CoreClockMHz, double? MemClockMHz,
                               double? PowerW, double? PowerLimitW, double? FanPercent, int? PState, ulong? ThrottleReasons);

/// <summary>Telemetria rápida (NVML) para os testes de GPU; não depende do LibreHardwareMonitor.</summary>
public sealed class GpuTelemetry
{
    private readonly IntPtr _handle;

    private GpuTelemetry(IntPtr handle, string name)
    {
        _handle = handle;
        Name = name;
    }

    public string Name { get; }

    /// <summary>Telemetria da placa NVIDIA correspondente ao adaptador (por device ID; senão a primeira).</summary>
    public static GpuTelemetry? ForAdapter(GpuAdapter? adapter)
    {
        if (!Nvml.Available) return null;
        var devices = Nvml.Devices();
        if (devices.Count == 0) return null;
        var match = adapter != null && adapter.VendorId == 0x10DE
            ? devices.FirstOrDefault(d => d.DeviceId == adapter.DeviceId) ?? devices[0]
            : adapter == null ? devices[0] : null;
        return match == null ? null : new GpuTelemetry(match.Handle, match.Name);
    }

    public GpuSample Read()
    {
        var h = _handle;
        double? W(uint? mw) => mw is { } v ? v / 1000.0 : null;
        return new GpuSample(
            Nvml.Temperature(h),
            Nvml.Util(h) is { } u ? u.Gpu : null,
            Nvml.Clock(h, Nvml.ClockGraphics),
            Nvml.Clock(h, Nvml.ClockMem),
            W(Nvml.PowerUsageMw(h)),
            W(Nvml.PowerLimitMw(h)),
            Nvml.FanPercent(h),
            Nvml.PState(h),
            Nvml.ThrottleReasons(h));
    }

    /// <summary>Motivos de limitação de clock (bits da NVML) em texto curto e neutro.</summary>
    public static IReadOnlyList<string> ThrottleFlags(ulong reasons)
    {
        var parts = new List<string>();
        if ((reasons & 0x4) != 0) parts.Add("power");
        if ((reasons & 0x60) != 0) parts.Add("thermal");
        if ((reasons & 0x8) != 0) parts.Add("hwslowdown");
        if ((reasons & 0x80) != 0) parts.Add("powerbrake");
        if (parts.Count == 0) parts.Add((reasons & 0x1) != 0 ? "idle" : "none");
        return parts;
    }
}
