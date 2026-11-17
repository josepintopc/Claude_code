using System.ComponentModel;
using LibreHardwareMonitor.Hardware;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.Hardware;

/// <summary>Linha do painel de sensores, com notificação de mudança para a interface.</summary>
public sealed class SensorReading : INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs ValueArgs = new(nameof(Value));
    private static readonly PropertyChangedEventArgs MinArgs = new(nameof(Min));
    private static readonly PropertyChangedEventArgs MaxArgs = new(nameof(Max));
    private static readonly PropertyChangedEventArgs CategoryArgs = new(nameof(Category));

    public string Id { get; init; } = "";
    public string Hardware { get; init; } = "";
    public string Name { get; init; } = "";
    public SensorType Type { get; init; }
    public HardwareType HardwareType { get; init; }

    private float? _value, _min, _max;
    public float? RawValue => _value;
    public float? RawMin => _min;
    public float? RawMax => _max;

    /// <summary>Nome do tipo (Temperatura, Carga...) no idioma atual.</summary>
    public string Category => CategoryName(Type);

    /// <summary>Ordem de exibição por tipo (clocks e temperaturas primeiro, como no GPU-Z).</summary>
    public int Order => Type switch
    {
        SensorType.Clock => 0, SensorType.Temperature => 1, SensorType.Load => 2, SensorType.Fan => 3,
        SensorType.Control => 4, SensorType.Power => 5, SensorType.Voltage => 6, SensorType.Current => 7,
        SensorType.SmallData => 8, SensorType.Data => 9, SensorType.Throughput => 10, _ => 11,
    };

    public string Value => Format(_value);
    public string Min => Format(_min);
    public string Max => Format(_max);

    public void Update(float? value, float? min, float? max)
    {
        bool v = _value != value, mi = _min != min, ma = _max != max;
        _value = value; _min = min; _max = max;
        if (v) PropertyChanged?.Invoke(this, ValueArgs);
        if (mi) PropertyChanged?.Invoke(this, MinArgs);
        if (ma) PropertyChanged?.Invoke(this, MaxArgs);
    }

    /// <summary>Chamado ao trocar de idioma.</summary>
    public void Relocalize() => PropertyChanged?.Invoke(this, CategoryArgs);

    private string Format(float? v)
    {
        if (v == null) return "—";
        var c = Loc.Culture;
        return Type switch
        {
            SensorType.Temperature => string.Format(c, "{0:0.#} °C", v),
            SensorType.Load or SensorType.Control or SensorType.Level or SensorType.Humidity => string.Format(c, "{0:0.#} %", v),
            SensorType.Clock => string.Format(c, "{0:0} MHz", v),
            SensorType.Power => string.Format(c, "{0:0.#} W", v),
            SensorType.Voltage => string.Format(c, "{0:0.###} V", v),
            SensorType.Current => string.Format(c, "{0:0.###} A", v),
            SensorType.Fan => string.Format(c, "{0:0} RPM", v),
            SensorType.Flow => string.Format(c, "{0:0.#} L/h", v),
            SensorType.Frequency => string.Format(c, "{0:0.#} Hz", v),
            SensorType.Data => string.Format(c, "{0:0.##} GB", v),
            SensorType.SmallData => string.Format(c, "{0:0.#} MB", v),
            SensorType.Throughput => string.Format(c, "{0:0.##} MB/s", v / 1048576f),
            SensorType.Energy => string.Format(c, "{0:0.#} mWh", v),
            _ => string.Format(c, "{0:0.##}", v),
        };
    }

    public static string CategoryName(SensorType t)
    {
        string name = t.ToString();
        string key = "sensor.type." + (t == SensorType.SmallData ? "data" : name.ToLowerInvariant());
        if (name == "TimeSpan") key = "sensor.type.timing";
        string text = Loc.T(key);
        return text == key ? name : text; // tipo novo da biblioteca ainda sem tradução
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
