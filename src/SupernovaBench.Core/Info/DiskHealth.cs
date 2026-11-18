using System.Buffers.Binary;

namespace SupernovaBench.Core.Info;

public enum HealthStatus { Unknown, Good, Caution, Bad }

/// <summary>Atributo SMART de um disco ATA/SATA.</summary>
public sealed record SmartAttribute(byte Id, int Current, int Worst, int Threshold, long Raw, ushort Flags)
{
    public string NameKey => SmartParser.AttributeNameKey(Id);

    /// <summary>Valor atual chegou ao limite do fabricante (o disco considera o atributo em falha).</summary>
    public bool Failing => Threshold > 0 && Current > 0 && Current <= Threshold;

    /// <summary>Valor bruto "legível": temperatura e horas usam só a parte baixa (o resto são campos extras do fabricante).</summary>
    public long DisplayRaw => Id switch
    {
        190 or 194 => Raw & 0xFF,
        9 or 12 or 5 or 196 or 197 or 198 or 199 => Raw & 0xFFFFFFFF,
        _ => Raw,
    };
}

/// <summary>Log de saúde do NVMe (página 0x02 da especificação).</summary>
public sealed record NvmeHealth
{
    public byte CriticalWarning { get; init; }
    public int TemperatureC { get; init; }
    public int AvailableSpare { get; init; }
    public int SpareThreshold { get; init; }
    public int PercentUsed { get; init; }
    public double DataReadBytes { get; init; }
    public double DataWrittenBytes { get; init; }
    public long PowerCycles { get; init; }
    public long PowerOnHours { get; init; }
    public long UnsafeShutdowns { get; init; }
    public long MediaErrors { get; init; }
    public long ErrorLogEntries { get; init; }
    public long WarningTempMinutes { get; init; }
    public long CriticalTempMinutes { get; init; }
}

/// <summary>Motivo do veredito (chave de tradução + valor).</summary>
public sealed record HealthNote(string Key, long Value);

/// <summary>Saúde de um disco físico, juntando o que o Windows e o próprio disco informam.</summary>
public sealed class DiskHealth
{
    public int DiskNumber { get; init; }
    public string Model { get; init; } = "";
    public string Bus { get; init; } = "";
    public string Media { get; init; } = "";
    public long SizeBytes { get; init; }
    public string Serial { get; init; } = "";
    public string Firmware { get; init; } = "";

    /// <summary>Origem dos dados: "nvme", "ata", "wmi" ou "" (nenhum).</summary>
    public string Source { get; set; } = "";

    public bool? PredictFailure { get; set; }
    public int? TemperatureC { get; set; }
    public int? TemperatureMaxC { get; set; }
    public int? WearPercent { get; set; }
    public long? PowerOnHours { get; set; }
    public long? PowerCycles { get; set; }
    public double? HostReadBytes { get; set; }
    public double? HostWrittenBytes { get; set; }
    public long? ReadErrors { get; set; }
    public long? WriteErrors { get; set; }
    public List<SmartAttribute> Attributes { get; set; } = new();
    public NvmeHealth? Nvme { get; set; }

    /// <summary>Por que não há dados (chave de tradução), quando for o caso.</summary>
    public string? UnavailableKey { get; set; }

    public HealthStatus Status { get; private set; }
    public int? LifePercent { get; private set; }
    public List<HealthNote> Notes { get; private set; } = new();

    public bool HasData => Source != "";

    /// <summary>Recalcula o veredito (chame depois de preencher os dados).</summary>
    public DiskHealth Evaluate()
    {
        var (status, life, notes) = SmartParser.Evaluate(this);
        Status = status;
        LifePercent = life;
        Notes = notes;
        return this;
    }
}

/// <summary>Interpreta os dados brutos de SMART (ATA) e do log de saúde NVMe, e decide o veredito no estilo CrystalDiskInfo.</summary>
public static class SmartParser
{
    private static readonly HashSet<byte> KnownIds = new()
    {
        1, 3, 4, 5, 7, 9, 10, 12, 169, 170, 171, 172, 173, 174, 177, 179, 180, 181, 182, 183, 184, 187, 188, 190, 191, 192, 193,
        194, 195, 196, 197, 198, 199, 202, 231, 232, 233, 235, 241, 242,
    };

    public static string AttributeNameKey(byte id) => KnownIds.Contains(id) ? $"smart.attr.{id}" : "smart.attr.unknown";

    /// <summary>
    /// Lê o bloco "VendorSpecific" de MSStorageDriver_FailurePredictData (2 bytes de versão + 30 atributos de 12 bytes)
    /// e, se houver, o de MSStorageDriver_FailurePredictThresholds (mesmo formato; o limite fica no 2º byte).
    /// </summary>
    public static List<SmartAttribute> ParseAta(ReadOnlySpan<byte> data, ReadOnlySpan<byte> thresholds = default)
    {
        var limits = new Dictionary<byte, int>();
        for (int i = 0; i < 30; i++)
        {
            int o = 2 + i * 12;
            if (o + 12 > thresholds.Length) break;
            byte id = thresholds[o];
            if (id != 0) limits[id] = thresholds[o + 1];
        }

        var list = new List<SmartAttribute>();
        for (int i = 0; i < 30; i++)
        {
            int o = 2 + i * 12;
            if (o + 12 > data.Length) break;
            byte id = data[o];
            if (id == 0) continue;
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(o + 1, 2));
            long raw = 0;
            for (int b = 5; b >= 0; b--) raw = (raw << 8) | data[o + 5 + b];
            list.Add(new SmartAttribute(id, data[o + 3], data[o + 4], limits.GetValueOrDefault(id), raw, flags));
        }
        return list;
    }

    /// <summary>Lê o log de saúde NVMe (512 bytes). Valores de 128 bits são saturados em 64 bits.</summary>
    public static NvmeHealth ParseNvme(ReadOnlySpan<byte> log)
    {
        if (log.Length < 512) throw new ArgumentException("NVMe health log must have 512 bytes", nameof(log));
        static long U128(ReadOnlySpan<byte> s, int offset)
        {
            ulong lo = BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(offset, 8));
            ulong hi = BinaryPrimitives.ReadUInt64LittleEndian(s.Slice(offset + 8, 8));
            return hi != 0 || lo > long.MaxValue ? long.MaxValue : (long)lo;
        }
        int kelvin = BinaryPrimitives.ReadUInt16LittleEndian(log.Slice(1, 2));
        return new NvmeHealth
        {
            CriticalWarning = log[0],
            TemperatureC = kelvin > 0 ? kelvin - 273 : 0,
            AvailableSpare = log[3],
            SpareThreshold = log[4],
            PercentUsed = log[5],
            DataReadBytes = U128(log, 32) * 512_000.0,
            DataWrittenBytes = U128(log, 48) * 512_000.0,
            PowerCycles = U128(log, 112),
            PowerOnHours = U128(log, 128),
            UnsafeShutdowns = U128(log, 144),
            MediaErrors = U128(log, 160),
            ErrorLogEntries = U128(log, 176),
            WarningTempMinutes = BinaryPrimitives.ReadUInt32LittleEndian(log.Slice(192, 4)),
            CriticalTempMinutes = BinaryPrimitives.ReadUInt32LittleEndian(log.Slice(196, 4)),
        };
    }

    /// <summary>Copia o que o log NVMe informa para os campos comuns.</summary>
    public static void Apply(DiskHealth d, NvmeHealth n)
    {
        d.Nvme = n;
        d.Source = "nvme";
        if (n.TemperatureC > 0) d.TemperatureC = n.TemperatureC;
        d.WearPercent = n.PercentUsed;
        d.PowerOnHours = n.PowerOnHours;
        d.PowerCycles = n.PowerCycles;
        d.HostReadBytes = n.DataReadBytes;
        d.HostWrittenBytes = n.DataWrittenBytes;
    }

    /// <summary>Copia o que os atributos ATA informam para os campos comuns.</summary>
    public static void Apply(DiskHealth d, IReadOnlyList<SmartAttribute> attrs, bool? predictFailure)
    {
        d.Attributes = attrs.ToList();
        d.Source = "ata";
        if (predictFailure != null) d.PredictFailure = predictFailure;
        SmartAttribute? A(byte id) => attrs.FirstOrDefault(a => a.Id == id);
        if ((A(194) ?? A(190)) is { } t && t.DisplayRaw is > 0 and < 120) d.TemperatureC = (int)t.DisplayRaw;
        if (A(9) is { } h) d.PowerOnHours = h.DisplayRaw;
        if (A(12) is { } c) d.PowerCycles = c.DisplayRaw;
        if (LifeFromAta(attrs) is { } life) d.WearPercent = 100 - life;
    }

    /// <summary>Vida restante (%) pelos atributos de desgaste de SSD, quando existem (valor normalizado atual).</summary>
    public static int? LifeFromAta(IReadOnlyList<SmartAttribute> attrs)
    {
        foreach (byte id in new byte[] { 231, 233, 177, 202, 169, 173 })
            if (attrs.FirstOrDefault(a => a.Id == id) is { } a && a.Current is > 0 and <= 100)
                return a.Current;
        return null;
    }

    public static (HealthStatus Status, int? Life, List<HealthNote> Notes) Evaluate(DiskHealth d)
    {
        var notes = new List<HealthNote>();
        bool bad = false, caution = false;

        if (d.PredictFailure == true) { bad = true; notes.Add(new HealthNote("health.note.predictFailure", 1)); }

        foreach (var a in d.Attributes.Where(a => a.Failing))
        {
            bad = true;
            notes.Add(new HealthNote("health.note.attrFailing", a.Id));
        }

        void Count(byte id, string key)
        {
            if (d.Attributes.FirstOrDefault(a => a.Id == id) is { } a && a.DisplayRaw > 0)
            {
                caution = true;
                notes.Add(new HealthNote(key, a.DisplayRaw));
            }
        }
        Count(5, "health.note.reallocated");
        Count(197, "health.note.pending");
        Count(198, "health.note.uncorrectable");

        if (d.Nvme is { } n)
        {
            if (n.CriticalWarning != 0) { bad = true; notes.Add(new HealthNote("health.note.criticalWarning", n.CriticalWarning)); }
            if (n.SpareThreshold > 0 && n.AvailableSpare < n.SpareThreshold) { bad = true; notes.Add(new HealthNote("health.note.spare", n.AvailableSpare)); }
            if (n.MediaErrors > 0) { caution = true; notes.Add(new HealthNote("health.note.mediaErrors", n.MediaErrors)); }
        }

        if (d.ReadErrors is > 0) { caution = true; notes.Add(new HealthNote("health.note.readErrors", d.ReadErrors.Value)); }
        if (d.WriteErrors is > 0) { caution = true; notes.Add(new HealthNote("health.note.writeErrors", d.WriteErrors.Value)); }

        int? life = d.WearPercent is { } w ? Math.Clamp(100 - w, 0, 100) : null;
        if (life is <= 10) { caution = true; notes.Add(new HealthNote("health.note.lifeLow", life.Value)); }

        bool anyData = d.HasData || d.PredictFailure != null || d.TemperatureC != null || d.WearPercent != null || d.PowerOnHours != null;
        var status = bad ? HealthStatus.Bad : caution ? HealthStatus.Caution : anyData ? HealthStatus.Good : HealthStatus.Unknown;
        return (status, life, notes);
    }
}
