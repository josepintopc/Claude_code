using System.Collections.ObjectModel;
using System.Management;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using SupernovaBench.Core.Localization;

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
        bool badHealthLog = hw.HardwareType == HardwareType.Storage
                            && NvmeHealthLooksInvalid(hw.Sensors.Select(s => (s.SensorType, s.Name, s.Value)));
        foreach (var s in hw.Sensors)
        {
            if (reset)
            {
                s.ResetMin();
                s.ResetMax();
            }
            bool hide = badHealthLog && !KeepWhenHealthLogInvalid(s.SensorType, s.Name);
            string id = s.Identifier.ToString();
            if (!_index.TryGetValue(id, out var row))
            {
                if (hide) continue;
                row = new SensorReading
                {
                    Id = id,
                    Hardware = HardwareName(hw),
                    Name = s.Name,
                    Type = s.SensorType,
                    HardwareType = hw.HardwareType,
                };
                _index[id] = row;
                poll.NewRows.Add(row);
            }
            if (hide)
                poll.Updates.Add((row, null, null, null));
            else
                poll.Updates.Add((row, Plausible(s.SensorType, s.Name, s.Value), Plausible(s.SensorType, s.Name, s.Min),
                                  Plausible(s.SensorType, s.Name, s.Max)));
        }
        foreach (var sub in hw.SubHardware)
            Walk(sub, poll, reset);
    }

    // ------------------------------------------------------------------ leituras que a biblioteca entrega erradas

    /// <summary>
    /// Temperatura de 0 °C ou abaixo é sensor sem leitura (a biblioteca começa em 0), não frio de verdade.
    /// "Distance to TjMax" é exceção: 0 ali significa CPU no limite térmico.
    /// </summary>
    public static float? Plausible(SensorType type, string name, float? value)
        => type == SensorType.Temperature && value <= 0 && !name.Contains("Distance", StringComparison.Ordinal) ? null : value;

    /// <summary>
    /// Em alguns SSDs NVMe (ex.: SK hynix com o driver do Windows) a biblioteca não consegue o log de saúde e mostra
    /// zeros e 255: temperatura 0, reserva 0% com limite 255%, uso 255%. Os dados certos ficam em "Saúde dos discos".
    /// </summary>
    public static bool NvmeHealthLooksInvalid(IEnumerable<(SensorType Type, string Name, float? Value)> sensors)
    {
        foreach (var (type, name, value) in sensors)
        {
            if (type == SensorType.Temperature && name == "Composite Temperature" && value is not > 0) return true;
            if (type == SensorType.Level && name is "Available Spare" or "Available Spare Threshold" && value > 100) return true;
        }
        return false;
    }

    /// <summary>Com o log de saúde inválido, só fica o que vem do Windows (atividade, velocidade, espaço) e os limites de temperatura.</summary>
    public static bool KeepWhenHealthLogInvalid(SensorType type, string name) => type switch
    {
        SensorType.Load or SensorType.Throughput => true,
        SensorType.Data => name.Contains("Space", StringComparison.Ordinal),
        SensorType.Temperature => name.StartsWith("Warning", StringComparison.Ordinal) || name.StartsWith("Critical", StringComparison.Ordinal),
        _ => false,
    };

    private Dictionary<int, string>? _diskModels;

    /// <summary>
    /// Nome sem os caracteres nulos e espaços com que alguns SSDs NVMe preenchem o campo de modelo
    /// (o SK hynix HFS001TEJ9X125N chega como 40 × '\0').
    /// </summary>
    public static string CleanName(string? name)
        => new string((name ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();

    /// <summary>Nome do grupo. Quando a biblioteca não tem o modelo do SSD, usa o do Windows (WMI).</summary>
    private string HardwareName(IHardware hw)
    {
        string name = CleanName(hw.Name);
        if (name.Length > 0) return name;
        string last = hw.Identifier.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? "";
        if (hw.HardwareType == HardwareType.Storage && int.TryParse(last, out int index))
        {
            _diskModels ??= DiskModels();
            return _diskModels.TryGetValue(index, out var model) ? model : Loc.T("sensors.disk", index);
        }
        return $"{hw.HardwareType} {last}".Trim();
    }

    private static Dictionary<int, string> DiskModels()
    {
        var map = new Dictionary<int, string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Index, Model FROM Win32_DiskDrive");
            foreach (var d in searcher.Get().Cast<ManagementObject>())
            {
                string model = d["Model"]?.ToString()?.Trim() ?? "";
                if (d["Index"] is { } i && model.Length > 0) map[Convert.ToInt32(i)] = model;
            }
        }
        catch
        {
            // Sem WMI: fica o nome genérico.
        }
        return map;
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
            var cores = Of(HardwareType.Cpu, SensorType.Load).Where(r => IsCoreSensorName(r.Name))
                                                             .Select(r => r.RawValue!.Value).ToList();
            return cores.Count > 0 ? cores.Max() : null;
        }
    }

    /// <summary>Média dos clocks dos núcleos (MHz).</summary>
    public float? CpuClock
    {
        get
        {
            var cores = Of(HardwareType.Cpu, SensorType.Clock).Where(r => IsCoreSensorName(r.Name))
                                                             .Select(r => r.RawValue!.Value).ToList();
            return cores.Count > 0 ? cores.Average() : null;
        }
    }

    private static readonly Regex CoreSensorName = new(@"^(CPU )?([PE]-)?Core #\d+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Sensor de um núcleo: "Core #1", "CPU Core #1" (e "CPU Core #1 Thread #2") ou, nas CPUs híbridas da Intel
    /// (Core 12ª geração em diante, Core Ultra), "P-Core #1" e "E-Core #1".
    /// </summary>
    public static bool IsCoreSensorName(string name) => CoreSensorName.IsMatch(name);

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
