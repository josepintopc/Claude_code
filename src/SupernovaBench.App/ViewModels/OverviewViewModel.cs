using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core.Benchmarks;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

public sealed partial class OverviewViewModel : LocalizedViewModel
{
    private readonly MainViewModel _main;

    public OverviewViewModel(MainViewModel main)
    {
        _main = main;
        AppServices.History.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshScores);
        RefreshScores();
        _ = LoadSummaryAsync();
    }

    public SensorService Sensors => AppServices.Sensors;
    public IEnumerable<NavItem> Shortcuts => _main.AvailableModules;

    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _cpu = "";
    [ObservableProperty] private string _gpu = "";
    [ObservableProperty] private string _ram = "";
    [ObservableProperty] private string _board = "";
    [ObservableProperty] private string _os = "";
    [ObservableProperty] private string _storage = "";

    [ObservableProperty] private string _multiScore = "—";
    [ObservableProperty] private string _singleScore = "—";
    [ObservableProperty] private bool _hasScore;

    private async Task LoadSummaryAsync()
    {
        try
        {
            var s = await AppServices.SystemInfo.GetSummaryAsync();
            (Cpu, Gpu, Ram, Board, Os, Storage) = (s.Cpu, s.Gpu, s.Ram, s.Board, s.Os, s.Storage);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            Cpu = ex.Message;
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void RefreshScores()
    {
        var multi = AppServices.History.Latest(TestIds.CpuMulti);
        var single = AppServices.History.Latest(TestIds.CpuSingle);
        MultiScore = multi?.Score.ToString("N0", Loc.Culture) ?? "—";
        SingleScore = single?.Score.ToString("N0", Loc.Culture) ?? "—";
        HasScore = multi != null || single != null;
    }

    [RelayCommand]
    private void Open(string key) => _main.Navigate(key);

    protected override void OnLanguageChanged()
    {
        RefreshScores();
        base.OnLanguageChanged();
    }
}
