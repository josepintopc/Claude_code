using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Localization;
using SupernovaBench.Hardware;

namespace SupernovaBench.App.ViewModels;

/// <summary>Tabela de sensores agrupada por hardware, com filtro.</summary>
public sealed partial class SensorsViewModel : LocalizedViewModel
{
    public SensorsViewModel()
    {
        View = CollectionViewSource.GetDefaultView(Sensors.Monitor.Rows);
        View.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SensorReading.Hardware)));
        View.SortDescriptions.Add(new SortDescription(nameof(SensorReading.Hardware), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(SensorReading.Order), ListSortDirection.Ascending));
        View.SortDescriptions.Add(new SortDescription(nameof(SensorReading.Name), ListSortDirection.Ascending));
        View.Filter = Matches;
        Sensors.Monitor.Rows.CollectionChanged += (_, _) => OnPropertyChanged(nameof(CountText));
    }

    public SensorService Sensors => AppServices.Sensors;
    public ICollectionView View { get; }

    public string CountText => Loc.T("sensors.count", Sensors.Monitor.Rows.Count);

    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => View.Refresh();

    private bool Matches(object o)
    {
        if (string.IsNullOrWhiteSpace(Filter)) return true;
        if (o is not SensorReading r) return false;
        var f = Filter.Trim();
        return r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.Hardware.Contains(f, StringComparison.OrdinalIgnoreCase)
            || r.Category.Contains(f, StringComparison.OrdinalIgnoreCase);
    }
}
