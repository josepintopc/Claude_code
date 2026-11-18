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
        AppServices.Games.Changed += (_, _) => System.Windows.Application.Current?.Dispatcher.InvokeAsync(RefreshRecentGames);
        RefreshScores();
        RefreshRecentGames();
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
    [ObservableProperty] private string _gpuScore = "—";
    [ObservableProperty] private string _memoryScore = "—";
    [ObservableProperty] private string _diskScore = "—";
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
        var gpu = AppServices.History.Latest(TestIds.GpuScene);
        var mem = AppServices.History.Latest(TestIds.MemoryBench);
        var disk = AppServices.History.Latest(TestIds.DiskBench);
        GpuScore = gpu?.Score.ToString("N0", Loc.Culture) ?? "—";
        MemoryScore = mem?.Score.ToString("N0", Loc.Culture) ?? "—";
        DiskScore = disk?.Score.ToString("N0", Loc.Culture) ?? "—";
        MultiScore = multi?.Score.ToString("N0", Loc.Culture) ?? "—";
        SingleScore = single?.Score.ToString("N0", Loc.Culture) ?? "—";
        HasScore = multi != null || single != null || gpu != null || mem != null || disk != null;
    }

    /// <summary>Até 4 jogos jogados por último (pelo Supernova ou pela Steam).</summary>
    public System.Collections.ObjectModel.ObservableCollection<GameCard> RecentGames { get; } = new();

    [ObservableProperty] private bool _hasRecentGames;

    private void RefreshRecentGames()
    {
        var games = AppServices.Games;
        var recent = games.Games.Where(g => !games.Store.IsHidden(g.Id) && games.LastPlayed(g) != null)
                                .OrderByDescending(g => games.LastPlayed(g)).Take(4).ToList();
        if (recent.Select(g => g.Id).SequenceEqual(RecentGames.Select(c => c.Game.Id)))
        {
            foreach (var c in RecentGames) c.Refresh();
            return;
        }
        RecentGames.Clear();
        foreach (var g in recent) RecentGames.Add(new GameCard(g));
        HasRecentGames = RecentGames.Count > 0;
    }

    [RelayCommand]
    private void Open(string key) => _main.Navigate(key);

    [RelayCommand]
    private void OpenGame(GameCard? card)
    {
        _main.Navigate("games");
        if (card != null) _main.Games.Select(card.Game.Id);
    }

    protected override void OnLanguageChanged()
    {
        foreach (var c in RecentGames) c.Refresh();
        RefreshScores();
        base.OnLanguageChanged();
    }
}
