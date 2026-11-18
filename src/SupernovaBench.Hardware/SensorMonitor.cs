using System.Collections.ObjectModel;
using LibreHardwareMonitor.Hardware;

namespace SupernovaBench.Hardware;

/// <summary>Leitura feita fora da thread da interface, aplicada depois com <see cref="SensorMonitor.Apply"/>.</summary>
public sealed class SensorPoll
{
    internal readonly List<(SensorReading Row, float? Value, float? Min, float? Max)> Updates = new();
    internal readonly List<SensorReading> NewRows = new();
}

/// <summary>
/// Encapsula o LibreHardwareMonitorLib. <see cref="Poll"/> lê o hardware (chame sempre da mesma thread de fundo,
/// a biblioteca não é thread-safe); <see cref="Apply"/> atualiza as linhas (chame na thread da interface).
/// <para>Baseado no SensorMonitor do ResetExplode (MIT, © 2026 Gustavo / GustavoReset).</para>
/// </summary>
public sealed class SensorMonitor : IDisposable
{
    private readonly Computer _computer;
    private readonly Dictionary<string, SensorReading> _index = new();
    private readonly object _gate = new();
    private volatile bool _open;
    private volatile bool _resetRequested;
    private bool _disposed;

    public ObservableCollection<SensorReading> Rows { get; } = new();
    public bool IsOpen => _open;
    public Exception? OpenError { get; private set; }

    public SensorMonitor()
    {
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = true,
            IsControllerEnabled = true,
            IsPsuEnabled = true,
            IsBatteryEnabled = true,
        };
    }

    /// <summary>Detecta o hardware (leva de 0,5 a alguns segundos) fora da thread da janela.</summary>
    public Task OpenAsync() => Task.Run(() =>
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                _computer.Open();
                _open = true;
            }
        }
        catch (Exception ex)
        {
            OpenError = ex;
        }
    });

    /// <summary>Zera mínimos e máximos na próxima leitura.</summary>
    public void RequestResetMinMax() => _resetRequested = true;

    public SensorPoll Poll()
    {
        var poll = new SensorPoll();
        if (!_open) return poll;
        lock (_gate)
        {
            if (_disposed) return poll;
            bool reset = _resetRequested;
            _resetRequested = false;
            foreach (var hw in _computer.Hardware)
                Walk(hw, poll, reset);
        }
        return poll;
    }

    private void Walk(IHardware hw, SensorPoll poll, bool reset)
    {
        try { hw.Update(); }
        catch { return; } // um dispositivo com problema não derruba os outros
        foreach (var s in hw.Sensors)
        {
            if (reset)
            {
                s.ResetMin();
                s.ResetMax();
            }
            string id = s.Identifier.ToString();
            if (!_index.TryGetValue(id, out var row))
            {
                row = new SensorReading
                {
                    Id = id,
                    Hardware = hw.Name,
                    Name = s.Name,
                    Type = s.SensorType,
                    HardwareType = hw.HardwareType,
                };
                _index[id] = row;
                poll.NewRows.Add(row);
            }
            poll.Updates.Add((row, s.Value, s.Min, s.Max));
        }
        foreach (var sub in hw.SubHardware)
            Walk(sub, poll, reset);
    }

    public void Apply(SensorPoll poll)
    {
        foreach (var r in poll.NewRows) Rows.Add(r);
        foreach (var (row, v, min, max) in poll.Updates) row.Update(v, min, max);
    }

    // ------------------------------------------------------------------ atalhos para a interface

    private IEnumerable<SensorReading> Of(HardwareType hw, SensorType type)
        => Rows.Where(r => r.HardwareType == hw && r.Type == type && r.RawValue != null);

    /// <summary>Temperatura principal da CPU: Package (Intel) / Tctl-Tdie (AMD), senão a maior entre os núcleos.</summary>
    public float? CpuTemperature
    {
        get
        {
            var temps = Of(HardwareType.Cpu, SensorType.Temperature).Where(r => r.RawValue > 0).ToList();
            var main = temps.FirstOrDefault(r => r.Name.Contains("Package") || r.Name.Contains("Tctl") || r.Name.Contains("Tdie"));
            return main?.RawValue ?? (temps.Count > 0 ? temps.Max(r => r.RawValue) : null);
        }
    }

    public float? CpuLoad => Of(HardwareType.Cpu, SensorType.Load).FirstOrDefault(r => r.Name == "CPU Total")?.RawValue;

    /// <summary>Consumo do pacote (W). Sem o driver (PawnIO) o sensor pode existir zerado: aí é "indisponível", não 0 W.</summary>
    public float? CpuPower => Of(HardwareType.Cpu, SensorType.Power).FirstOrDefault(r => r.Name.Contains("Package") && r.RawValue > 0)?.RawValue;

    /// <summary>Carga do núcleo (ou thread) mais ocupado: revela jogos presos em uma thread só.</summary>
    public float? CpuMaxCoreLoad
    {
        get
        {
            var cores = Of(HardwareType.Cpu, SensorType.Load).Where(r => r.Name.StartsWith("CPU Core #", StringComparison.Ordinal))
                                                             .Select(r => r.RawValue!.Value).ToList();
            return cores.Count > 0 ? cores.Max() : null;
        }
    }

    /// <summary>Média dos clocks dos núcleos (MHz).</summary>
    public float? CpuClock
    {
        get
        {
            var cores = Of(HardwareType.Cpu, SensorType.Clock).Where(r => r.Name.StartsWith("Core #") || r.Name.StartsWith("CPU Core #"))
                                                             .Select(r => r.RawValue!.Value).ToList();
            return cores.Count > 0 ? cores.Average() : null;
        }
    }

    /// <summary>GPU dedicada (NVIDIA/AMD) tem prioridade sobre a integrada (Intel).</summary>
    private HardwareType? MainGpu
    {
        get
        {
            foreach (var t in new[] { HardwareType.GpuNvidia, HardwareType.GpuAmd, HardwareType.GpuIntel })
                if (Rows.Any(r => r.HardwareType == t)) return t;
            return null;
        }
    }

    public float? GpuTemperature
    {
        get
        {
            if (MainGpu is not { } gpu) return null;
            var temps = Of(gpu, SensorType.Temperature).ToList();
            return (temps.FirstOrDefault(r => r.Name == "GPU Core") ?? temps.FirstOrDefault())?.RawValue;
        }
    }

    public float? GpuLoad
    {
        get
        {
            if (MainGpu is not { } gpu) return null;
            var loads = Of(gpu, SensorType.Load).ToList();
            return (loads.FirstOrDefault(r => r.Name == "GPU Core") ?? loads.FirstOrDefault(r => r.Name == "D3D 3D"))?.RawValue;
        }
    }

    /// <summary>Uso da memória de vídeo (%): sensor de carga "GPU Memory" ou usada ÷ total.</summary>
    public float? GpuMemoryLoad
    {
        get
        {
            if (MainGpu is not { } gpu) return null;
            if (Of(gpu, SensorType.Load).FirstOrDefault(r => r.Name == "GPU Memory") is { } load) return load.RawValue;
            var used = Of(gpu, SensorType.SmallData).FirstOrDefault(r => r.Name == "GPU Memory Used")?.RawValue;
            var total = Of(gpu, SensorType.SmallData).FirstOrDefault(r => r.Name == "GPU Memory Total")?.RawValue;
            return used is { } u && total is > 0 ? 100 * u / total : null;
        }
    }

    public string? GpuName => MainGpu is { } gpu ? Rows.FirstOrDefault(r => r.HardwareType == gpu)?.Hardware : null;

    public enum CpuSensorProblem { None, NotElevated, PawnIOMissing, ReadFailed }

    /// <summary>Por que a temperatura/consumo da CPU não aparecem (o motivo mais provável primeiro).</summary>
    public CpuSensorProblem DiagnoseCpuSensors()
    {
        if (CpuTemperature != null) return CpuSensorProblem.None;
        if (!Elevation.IsElevated) return CpuSensorProblem.NotElevated;
        if (!PawnIO.IsInstalled) return CpuSensorProblem.PawnIOMissing;
        return CpuSensorProblem.ReadFailed;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_open)
            {
                try { _computer.Close(); } catch { }
            }
        }
    }
}
