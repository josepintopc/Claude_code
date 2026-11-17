using SupernovaBench.Graphics;

namespace SupernovaBench.App.Services;

/// <summary>Placa de vídeo principal e telemetria NVML, compartilhadas pelas páginas de GPU.</summary>
public sealed class GpuService
{
    private Task<(GpuAdapter? Adapter, GpuTelemetry? Telemetry)>? _init;

    private Task<(GpuAdapter? Adapter, GpuTelemetry? Telemetry)> Init()
        => _init ??= Task.Run(() =>
        {
            GpuAdapter? adapter = null;
            GpuTelemetry? telemetry = null;
            try
            {
                adapter = GpuAdapters.PickMain(GpuAdapters.Enumerate());
                telemetry = GpuTelemetry.ForAdapter(adapter);
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
            return (adapter, telemetry);
        });

    public async Task<GpuAdapter?> GetMainAdapterAsync() => (await Init()).Adapter;

    public async Task<GpuTelemetry?> GetTelemetryAsync() => (await Init()).Telemetry;

    /// <summary>Leitura da NVML (se houver); null sem NVIDIA.</summary>
    public GpuSample? ReadSample()
    {
        if (_init is not { IsCompletedSuccessfully: true } t || t.Result.Telemetry is not { } tel) return null;
        try { return tel.Read(); }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    /// <summary>Temperatura da GPU: NVML primeiro (não depende do nome do sensor), senão LibreHardwareMonitor.</summary>
    public double? ReadTemperature() => ReadSample()?.TemperatureC ?? AppServices.Sensors.Monitor.GpuTemperature;
}
