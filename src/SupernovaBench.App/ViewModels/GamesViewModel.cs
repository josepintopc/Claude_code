using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SupernovaBench.App.Services;
using SupernovaBench.Core;
using SupernovaBench.Core.Games;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.ViewModels;

/// <summary>Um jogo na grade: capa (carregada só quando aparece na tela), nome, loja e tempo de jogo.</summary>
public sealed partial class GameCard : ObservableObject
{
    private bool _coverRequested;

    public GameCard(GameEntry game)
    {
        Game = game;
        Refresh();
    }

    public GameEntry Game { get; }
    public string Name => Game.Name;
    public string StoreKey => GameEntry.StoreKey(Game.Store);
    public string StoreName => GameEntry.StoreName(Game.Store);

    public string Initials
    {
        get
        {
            var words = Game.Name.Split(new[] { ' ', ':', '-', '_' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpperInvariant(w[0]));
            string s = string.Concat(words);
            return s.Length > 0 ? s : "?";
        }
    }

    [ObservableProperty] private string _playtimeText = "";
    [ObservableProperty] private string _lastPlayedText = "";
    [ObservableProperty] private bool _isFavorite;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _coverIsIcon;
    [ObservableProperty] private bool _hasCover;

    private ImageSource? _cover;

    /// <summary>Capa: o primeiro acesso (quando o cartão aparece na tela) dispara o carregamento.</summary>
    public ImageSource? Cover
    {
        get
        {
            if (!_coverRequested)
            {
                _coverRequested = true;
                _ = LoadCoverAsync();
            }
            return _cover;
        }
    }

    private async Task LoadCoverAsync()
    {
        var c = await AppServices.Covers.GetAsync(Game, AppServices.Games.KnownExe(Game));
        if (c == null) return;
        _cover = c.Image;
        CoverIsIcon = c.IsIcon;
        HasCover = true;
        OnPropertyChanged(nameof(Cover));
    }

    public void Refresh()
    {
        var games = AppServices.Games;
        PlaytimeText = FormatPlaytime(games.Minutes(Game));
        LastPlayedText = games.LastPlayed(Game) is { } last ? FormatLastPlayed(last) : "";
        IsFavorite = games.Store.IsFavorite(Game.Id);
        IsHidden = games.Store.IsHidden(Game.Id);
        IsRunning = games.IsRunning(Game);
        OnPropertyChanged(nameof(StoreName));
    }

    public static string FormatPlaytime(double minutes) => minutes switch
    {
        <= 0 => Loc.T("games.never"),
        < 60 => Loc.T("games.minutes", Math.Max(1, (int)Math.Round(minutes))),
        _ => Loc.T("games.hours", (minutes / 60).ToString(minutes < 600 ? "0.0" : "N0", Loc.Culture)),
    };

    public static string FormatLastPlayed(DateTimeOffset when)
    {
        var days = (DateTime.Now.Date - when.LocalDateTime.Date).TotalDays;
        return days switch
        {
            < 1 => Loc.T("games.today"),
            < 2 => Loc.T("games.yesterday"),
            < 30 => Loc.T("games.daysAgo", (int)days),
            _ => when.LocalDateTime.ToString("d", Loc.Culture),
        };
    }
}

/// <summary>Uma linha da grade (a lista é virtualizada por linhas).</summary>
public sealed record GameRow(IReadOnlyList<GameCard> Cards);

/// <summary>Painel de detalhes do jogo selecionado, com o perfil de desempenho.</summary>
public sealed partial class GameDetails : LocalizedViewModel
{
    public GameDetails(GameCard card)
    {
        Card = card;
        var p = AppServices.Games.Store.Profile(card.Game.Id);
        PowerPlans = new[]
        {
            Opt(PowerPlanChoice.Keep, "games.power.keep", p.PowerPlan, SetPower),
            Opt(PowerPlanChoice.HighPerformance, "games.power.high", p.PowerPlan, SetPower),
            Opt(PowerPlanChoice.Ultimate, "games.power.ultimate", p.PowerPlan, SetPower),
            Opt(PowerPlanChoice.Balanced, "games.power.balanced", p.PowerPlan, SetPower),
        };
        Priorities = new[]
        {
            Opt(PriorityChoice.Normal, "games.priority.normal", p.Priority, SetPriority),
            Opt(PriorityChoice.AboveNormal, "games.priority.above", p.Priority, SetPriority),
            Opt(PriorityChoice.High, "games.priority.high", p.Priority, SetPriority),
        };
        Gpus = new[]
        {
            Opt(GpuChoice.Default, "games.gpu.default", p.Gpu, SetGpu),
            Opt(GpuChoice.HighPerformance, "games.gpu.high", p.Gpu, SetGpu),
            Opt(GpuChoice.PowerSaving, "games.gpu.saving", p.Gpu, SetGpu),
        };
    }

    private Option Opt<T>(T value, string key, T current, Action<T> set) where T : struct, Enum
        => new(value.ToString(), () => Loc.T(key), EqualityComparer<T>.Default.Equals(value, current), _ => set(value));

    public GameCard Card { get; }
    public GameEntry Game => Card.Game;
    public IReadOnlyList<Option> PowerPlans { get; }
    public IReadOnlyList<Option> Priorities { get; }
    public IReadOnlyList<Option> Gpus { get; }

    public string InstallDir => Game.InstallDir;
    public string SizeText => Game.SizeBytes is { } b && b > 0 ? Formats.Bytes(b) : "—";
    public string ExeText => AppServices.Games.KnownExe(Game) ?? Loc.T("games.exe.unknown");
    public bool GpuEnabled => AppServices.Games.KnownExe(Game) != null;
    public bool IsManual => Game.Store == GameStore.Manual;
    public string SessionsText => (AppServices.Games.Store.Stats(Game.Id)?.Sessions ?? 0).ToString(Loc.Culture);

    private GameProfile Current => AppServices.Games.Store.Profile(Game.Id);

    private void SetPower(PowerPlanChoice v) => AppServices.Games.SetProfile(Game, Current with { PowerPlan = v });
    private void SetPriority(PriorityChoice v) => AppServices.Games.SetProfile(Game, Current with { Priority = v });
    private void SetGpu(GpuChoice v) => AppServices.Games.SetProfile(Game, Current with { Gpu = v });

    public void Refresh()
    {
        OnPropertyChanged(nameof(ExeText));
        OnPropertyChanged(nameof(GpuEnabled));
        OnPropertyChanged(nameof(SessionsText));
    }
}

/// <summary>Biblioteca de jogos (estilo Playnite / GOG Galaxy).</summary>
public sealed partial class GamesViewModel : LocalizedViewModel
{
    private readonly Dictionary<string, GameCard> _cards = new();
    private readonly DispatcherTimer _clock;
    private string _store = "all";
    private string _sort = "recent";
    private int _columns = 5;

    public GamesViewModel()
    {
        Sorts = new[]
        {
            new Option("recent", () => Loc.T("games.sort.recent"), true, o => { _sort = o.Value; Rebuild(); }),
            new Option("name", () => Loc.T("games.sort.name"), false, o => { _sort = o.Value; Rebuild(); }),
            new Option("playtime", () => Loc.T("games.sort.playtime"), false, o => { _sort = o.Value; Rebuild(); }),
        };
        var games = AppServices.Games;
        games.Changed += (_, _) => Application.Current?.Dispatcher.InvokeAsync(OnLibraryChanged);
        games.PlayingChanged += (_, _) => Application.Current?.Dispatcher.InvokeAsync(UpdateNowPlaying);
        games.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GameService.IsScanning)) OnPropertyChanged(nameof(IsScanning));
        };
        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clock.Tick += (_, _) => UpdateNowPlaying();
        _clock.Start();
        OnLibraryChanged();
        _ = games.EnsureScannedAsync();
    }

    public ObservableCollection<GameRow> Rows { get; } = new();
    public ObservableCollection<Option> StoreFilters { get; } = new();
    public IReadOnlyList<Option> Sorts { get; }

    public bool IsScanning => AppServices.Games.IsScanning;

    [ObservableProperty] private string _search = "";
    [ObservableProperty] private bool _showHidden;
    [ObservableProperty] private string _countText = "";
    [ObservableProperty] private string _missingStoresText = "";
    [ObservableProperty] private string _nowPlayingText = "";
    [ObservableProperty] private bool _hasNowPlaying;
    [ObservableProperty] private bool _isEmpty;
    [ObservableProperty] private bool _noResults;
    [ObservableProperty] private GameDetails? _details;

    partial void OnSearchChanged(string value) => Rebuild();
    partial void OnShowHiddenChanged(bool value) => Rebuild();

    /// <summary>A tela informa quantas capas cabem por linha.</summary>
    public void SetColumns(int columns)
    {
        columns = Math.Max(1, columns);
        if (columns == _columns) return;
        _columns = columns;
        Rebuild();
    }

    private void OnLibraryChanged()
    {
        var games = AppServices.Games.Games;
        foreach (var g in games)
        {
            if (_cards.TryGetValue(g.Id, out var c) && c.Game == g) c.Refresh();
            else _cards[g.Id] = new GameCard(g);
        }
        foreach (var id in _cards.Keys.Except(games.Select(g => g.Id)).ToList()) _cards.Remove(id);
        if (Details != null && !_cards.ContainsKey(Details.Game.Id)) Details = null;
        Details?.Refresh();

        RebuildStoreFilters();
        UpdateNowPlaying();
        Rebuild();
    }

    private void RebuildStoreFilters()
    {
        var visible = _cards.Values.Where(c => !c.IsHidden).ToList();
        var stores = visible.GroupBy(c => c.Game.Store).OrderBy(g => g.Key).ToList();
        if (_store != "all" && stores.All(s => GameEntry.StoreKey(s.Key) != _store)) _store = "all";
        StoreFilters.Clear();
        StoreFilters.Add(new Option("all", () => Loc.T("games.store.all", visible.Count), _store == "all", o => { _store = o.Value; Rebuild(); }));
        foreach (var s in stores)
        {
            string key = GameEntry.StoreKey(s.Key);
            int count = s.Count();
            StoreFilters.Add(new Option(key, () => $"{GameEntry.StoreName(s.Key)} ({count})", _store == key, o => { _store = o.Value; Rebuild(); }));
        }

        var all = new[] { GameStore.Steam, GameStore.Epic, GameStore.Gog, GameStore.Ubisoft, GameStore.Ea, GameStore.BattleNet, GameStore.Xbox };
        var missing = all.Where(s => !AppServices.Games.StoresFound.Contains(s)).Select(GameEntry.StoreName).ToList();
        MissingStoresText = AppServices.Games.HasScanned && missing.Count > 0 ? Loc.T("games.missingStores", string.Join(", ", missing)) : "";
    }

    private void Rebuild()
    {
        IEnumerable<GameCard> list = _cards.Values.Where(c => ShowHidden || !c.IsHidden);
        if (_store != "all") list = list.Where(c => c.StoreKey == _store);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            string q = CoverSearch.Normalize(Search);
            list = list.Where(c => CoverSearch.Normalize(c.Name).Contains(q, StringComparison.Ordinal));
        }
        var games = AppServices.Games;
        list = _sort switch
        {
            "name" => list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            "playtime" => list.OrderByDescending(c => games.Minutes(c.Game)).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => list.OrderByDescending(c => c.IsRunning)
                     .ThenByDescending(c => c.IsFavorite)
                     .ThenByDescending(c => games.LastPlayed(c.Game) ?? DateTimeOffset.MinValue)
                     .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase),
        };
        var cards = list.ToList();

        Rows.Clear();
        for (int i = 0; i < cards.Count; i += _columns)
            Rows.Add(new GameRow(cards.Skip(i).Take(_columns).ToList()));

        int total = _cards.Values.Count(c => !c.IsHidden);
        CountText = Loc.T("games.count", total);
        IsEmpty = AppServices.Games.HasScanned && total == 0;
        NoResults = !IsEmpty && cards.Count == 0 && _cards.Count > 0;
    }

    private void UpdateNowPlaying()
    {
        var playing = AppServices.Games.NowPlaying;
        HasNowPlaying = playing.Count > 0;
        NowPlayingText = string.Join("  ·  ", playing.Select(p =>
            Loc.T("games.nowPlaying", p.Game.Name, Formats.Time(DateTimeOffset.Now - p.Start))));
        foreach (var c in _cards.Values) c.IsRunning = playing.Any(p => p.Game.Id == c.Game.Id);
    }

    /// <summary>Seleciona um jogo pelo id (atalho da Visão geral).</summary>
    public void Select(string id)
    {
        if (_cards.TryGetValue(id, out var card)) Open(card);
    }

    [RelayCommand]
    private void Open(GameCard? card) => Details = card == null ? null : new GameDetails(card);

    [RelayCommand]
    private void CloseDetails() => Details = null;

    [RelayCommand]
    private void Play(GameCard? card)
    {
        if (card == null) return;
        try
        {
            AppServices.Games.Launch(card.Game);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            MessageBox.Show(Loc.T("games.launchFailed", card.Name, ex.Message), AppInfo.Name, MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void OpenFolder(GameCard? card)
    {
        if (card != null) AppServices.Games.OpenFolder(card.Game);
    }

    [RelayCommand]
    private void ToggleFavorite(GameCard? card)
    {
        if (card != null) AppServices.Games.SetFavorite(card.Game, !card.IsFavorite);
    }

    [RelayCommand]
    private void ToggleHidden(GameCard? card)
    {
        if (card != null) AppServices.Games.SetHidden(card.Game, !card.IsHidden);
    }

    [RelayCommand]
    private void Remove(GameCard? card)
    {
        if (card == null || card.Game.Store != GameStore.Manual) return;
        AppServices.Games.RemoveManual(card.Game);
    }

    [RelayCommand]
    private Task Refresh() => AppServices.Games.RescanAsync();

    [RelayCommand]
    private void AddGame()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("games.add.filter") + " (*.exe)|*.exe",
            Title = Loc.T("games.add"),
        };
        if (dlg.ShowDialog() != true) return;
        string exe = dlg.FileName;
        string name = "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exe);
            name = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName! : info.FileDescription ?? "";
        }
        catch (Exception) { }
        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(exe);
        var g = AppServices.Games.AddManual(name.Trim(), exe);
        Select(g.Id);
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        foreach (var c in _cards.Values) c.Refresh();
        RebuildStoreFilters();
        UpdateNowPlaying();
        Rebuild();
    }
}
