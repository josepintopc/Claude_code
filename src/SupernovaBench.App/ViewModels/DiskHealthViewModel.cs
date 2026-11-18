using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Info;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.ViewModels;

public sealed record HealthLine(string Label, string Value);

public sealed record SmartRow(string Id, string Name, string Current, string Worst, string Threshold, string Raw, bool Failing);

/// <summary>Um disco físico na página Saúde dos discos (estilo CrystalDiskInfo).</summary>
public sealed partial class DiskHealthItem : ObservableObject
{
    private readonly Action<DiskHealthItem> _onSelected;

    public DiskHealthItem(DiskHealth disk, bool selected, Action<DiskHealthItem> onSelected)
    {
        Disk = disk;
        _isSelected = selected;
        _onSelected = onSelected;
        Rebuild();
    }

    public DiskHealth Disk { get; }

    [ObservableProperty] private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) _onSelected(this);
    }

    public string ChipLabel => $"{Disk.DiskNumber}: {Disk.Model}";
    public string Title => Disk.Model;
    public bool IsGood => Disk.Status == HealthStatus.Good;
    public bool IsCaution => Disk.Status == HealthStatus.Caution;
    public bool IsBad => Disk.Status == HealthStatus.Bad;
    public bool IsUnknown => Disk.Status == HealthStatus.Unknown;
    public bool HasAttributes => Attributes.Count > 0;

    public string StatusText { get; private set; } = "";
    public string LifeText { get; private set; } = "";
    public string TemperatureText { get; private set; } = "";
    public string Subtitle { get; private set; } = "";
    public string? UnavailableText { get; private set; }
    public List<HealthLine> Summary { get; private set; } = new();
    public List<string> Notes { get; private set; } = new();
    public List<SmartRow> Attributes { get; private set; } = new();

    /// <summary>Monta os textos no idioma atual.</summary>
    public void Rebuild()
    {
        var d = Disk;
        var c = Loc.Culture;
        StatusText = d.Status switch
        {
            HealthStatus.Good => Loc.T("health.status.good"),
            HealthStatus.Caution => Loc.T("health.status.caution"),
            HealthStatus.Bad => Loc.T("health.status.bad"),
            _ => Loc.T("health.status.unknown"),
        };
        LifeText = d.LifePercent is { } life ? Loc.T("health.life", life) : "";
        TemperatureText = d.TemperatureC is { } t ? $"{t} °C" : "—";
        Subtitle = string.Join(" · ", new[] { d.Bus, d.Media, d.SizeBytes > 0 ? Formats.Bytes(d.SizeBytes) : "" }.Where(s => !string.IsNullOrWhiteSpace(s)));
        UnavailableText = d.UnavailableKey != null ? Loc.T(d.UnavailableKey) : null;

        var lines = new List<HealthLine>();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) lines.Add(new HealthLine(Loc.T(key), value));
        }
        Add("health.temperature", d.TemperatureC is { } tc ? $"{tc} °C" + (d.TemperatureMaxC is { } tm ? Loc.T("health.tempMax", tm) : "") : null);
        Add("health.powerOnHours", d.PowerOnHours is { } h ? Loc.T("health.hours", h.ToString("N0", c)) + $" ({(h / 24.0 / 365).ToString("0.0", c)} {Loc.T("health.years")})" : null);
        Add("health.powerCycles", d.PowerCycles?.ToString("N0", c));
        Add("health.read", d.HostReadBytes is { } rb ? Tb(rb) : null);
        Add("health.written", d.HostWrittenBytes is { } wb ? Tb(wb) : null);
        if (d.Nvme is { } n)
        {
            Add("health.nvme.spare", $"{n.AvailableSpare}% ({Loc.T("health.nvme.threshold", n.SpareThreshold)})");
            Add("health.nvme.used", $"{n.PercentUsed}%");
            Add("health.nvme.unsafe", n.UnsafeShutdowns.ToString("N0", c));
            Add("health.nvme.media", n.MediaErrors.ToString("N0", c));
            Add("health.nvme.errlog", n.ErrorLogEntries.ToString("N0", c));
            Add("health.nvme.warnTemp", n.WarningTempMinutes > 0 ? Loc.T("health.minutes", n.WarningTempMinutes) : "0");
            Add("health.nvme.critical", n.CriticalWarning == 0 ? Loc.T("health.none") : $"0x{n.CriticalWarning:X2}");
        }
        Add("health.readErrors", d.ReadErrors?.ToString("N0", c));
        Add("health.writeErrors", d.WriteErrors?.ToString("N0", c));
        Add("health.firmware", d.Firmware);
        Add("health.source", d.Source switch
        {
            "nvme" => Loc.T("health.source.nvme"),
            "ata" => Loc.T("health.source.ata"),
            "wmi" => Loc.T("health.source.wmi"),
            _ => null,
        });
        Summary = lines;

        Notes = d.Notes.Select(n => Loc.T(n.Key, n.Value.ToString("N0", c))).ToList();
        Attributes = d.Attributes.Select(a => new SmartRow(
            a.Id.ToString("X2"), Loc.T(a.NameKey), a.Current.ToString(c), a.Worst.ToString(c),
            a.Threshold > 0 ? a.Threshold.ToString(c) : "—", a.DisplayRaw.ToString("N0", c), a.Failing)).ToList();

        OnPropertyChanged(string.Empty);
    }

    private static string Tb(double bytes)
        => bytes >= 1e12 ? string.Format(Loc.Culture, "{0:0.00} TB", bytes / 1e12) : string.Format(Loc.Culture, "{0:0.0} GB", bytes / 1e9);
}

/// <summary>Saúde dos discos: veredito Bom/Atenção/Ruim, vida restante, temperatura e atributos SMART.</summary>
public sealed partial class DiskHealthViewModel : LocalizedViewModel
{
    private int? _selectedNumber;

    public DiskHealthViewModel()
    {
        _ = LoadAsync();
    }

    public ObservableCollection<DiskHealthItem> Disks { get; } = new();

    [ObservableProperty] private DiskHealthItem? _selected;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isEmpty;

    [RelayCommand]
    private Task Refresh() => LoadAsync();

    private async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var disks = await Task.Run(DiskInfo.ReadHealth);
            Disks.Clear();
            int? select = disks.Any(d => d.DiskNumber == _selectedNumber) ? _selectedNumber : disks.FirstOrDefault()?.DiskNumber;
            foreach (var d in disks)
            {
                var item = new DiskHealthItem(d, d.DiskNumber == select, OnSelected);
                Disks.Add(item);
                if (item.IsSelected) Selected = item;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            IsLoading = false;
            IsEmpty = Disks.Count == 0;
        }
    }

    private void OnSelected(DiskHealthItem item)
    {
        foreach (var other in Disks)
            if (!ReferenceEquals(other, item) && other.IsSelected) other.IsSelected = false;
        _selectedNumber = item.Disk.DiskNumber;
        Selected = item;
    }

    protected override void OnLanguageChanged()
    {
        foreach (var d in Disks) d.Rebuild();
        base.OnLanguageChanged();
    }
}
