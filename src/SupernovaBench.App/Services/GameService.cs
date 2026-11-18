using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using SupernovaBench.Core.Games;
using SupernovaBench.Games;

namespace SupernovaBench.App.Services;

/// <summary>
/// Biblioteca de jogos: busca nos launchers, conta o tempo de jogo olhando os processos (a cada 10 s) e aplica o
/// perfil de desempenho quando um jogo abre (plano de energia e prioridade), desfazendo quando ele fecha.
/// </summary>
public sealed partial class GameService : ObservableObject
{
    private readonly PlaytimeTracker _tracker = new();
    private readonly DispatcherTimer _timer;
    private Task? _scan;
    private bool _ticking;
    private Guid? _planBefore;
    private string? _planOwner;

    public GameService()
    {
        Store = new GameLibraryStore();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public GameLibraryStore Store { get; }
    public IReadOnlyList<GameEntry> Games { get; private set; } = Array.Empty<GameEntry>();
    public IReadOnlyCollection<GameStore> StoresFound { get; private set; } = Array.Empty<GameStore>();
    public IReadOnlyDictionary<GameStore, string> Errors { get; private set; } = new Dictionary<GameStore, string>();

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasScanned;

    /// <summary>A lista de jogos ou as estatísticas mudaram.</summary>
    public event EventHandler? Changed;

    /// <summary>Um jogo abriu ou fechou.</summary>
    public event EventHandler? PlayingChanged;

    public IReadOnlyList<(GameEntry Game, DateTimeOffset Start, string? Exe)> NowPlaying => _tracker.Running;

    /// <summary>Começa a busca e a contagem de tempo (chamado ao abrir o app).</summary>
    public void Start()
    {
        _ = EnsureScannedAsync();
        _timer.Start();
    }

    public Task EnsureScannedAsync() => _scan ??= ScanAsync();

    public Task RescanAsync() => _scan = ScanAsync();

    private async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            var manual = Store.ManualGames();
            var result = await Task.Run(() => GameScanner.ScanAll(manual));
            Games = result.Games;
            StoresFound = result.StoresFound;
            Errors = result.Errors;
            foreach (var (store, error) in result.Errors) App.Log(new Exception($"games/{store}: {error}"));
            _tracker.SetGames(Games);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            IsScanning = false;
            HasScanned = true;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // ------------------------------------------------------------------ dados por jogo

    /// <summary>Tempo total: o da loja quando ela informa (Steam), senão o contado pelo Supernova.</summary>
    public double Minutes(GameEntry g)
    {
        double own = Store.Stats(g.Id)?.Minutes ?? 0;
        return g.StorePlaytimeMinutes is { } store ? Math.Max(store, own) : own;
    }

    public DateTimeOffset? LastPlayed(GameEntry g)
    {
        var own = Store.Stats(g.Id)?.LastPlayed;
        if (g.StoreLastPlayed is { } s && (own == null || s > own)) return s;
        return own;
    }

    /// <summary>Executável do jogo: o visto rodando ou o informado pela loja.</summary>
    public string? KnownExe(GameEntry g) => Store.Stats(g.Id)?.Exe ?? g.Executable;

    public bool IsRunning(GameEntry g) => _tracker.Running.Any(r => r.Game.Id == g.Id);

    public void Launch(GameEntry g) => GameLauncher.Launch(g);

    public void OpenFolder(GameEntry g) => GameLauncher.OpenFolder(g);

    public void SetProfile(GameEntry g, GameProfile profile)
    {
        var before = Store.Profile(g.Id);
        Store.SetProfile(g.Id, profile);
        if (before.Gpu != profile.Gpu && KnownExe(g) is { } exe)
        {
            try { GpuPreference.Set(exe, profile.Gpu); }
            catch (Exception ex) { App.Log(ex); }
        }
        TrySave();
    }

    public GameEntry AddManual(string name, string exe)
    {
        var g = Store.AddManual(name, exe);
        TrySave();
        Games = Games.Append(g).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        _tracker.SetGames(Games);
        Changed?.Invoke(this, EventArgs.Empty);
        return g;
    }

    public void RemoveManual(GameEntry g)
    {
        if (!Store.RemoveManual(g.Id)) return;
        TrySave();
        Games = Games.Where(x => x.Id != g.Id).ToList();
        _tracker.SetGames(Games);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetHidden(GameEntry g, bool hidden)
    {
        Store.SetHidden(g.Id, hidden);
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetFavorite(GameEntry g, bool favorite)
    {
        Store.SetFavorite(g.Id, favorite);
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    // ------------------------------------------------------------------ contagem de tempo e perfis

    private async Task TickAsync()
    {
        if (_ticking || Games.Count == 0) return;
        _ticking = true;
        try
        {
            var exes = await Task.Run(() => ProcessWatcher.Snapshot().Select(p => p.Path).ToList());
            Handle(_tracker.Update(exes, DateTimeOffset.Now));
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            _ticking = false;
        }
    }

    private void Handle(List<TrackerEvent> events)
    {
        if (events.Count == 0) return;
        bool playingChanged = false;
        var now = DateTimeOffset.Now;
        foreach (var e in events)
        {
            Store.Apply(e, now);
            switch (e.Kind)
            {
                case TrackerEventKind.Started:
                    ApplyProfile(e.Game, e.Exe);
                    playingChanged = true;
                    break;
                case TrackerEventKind.Ended:
                    RestoreProfile(e.Game);
                    playingChanged = true;
                    break;
            }
        }
        TrySave();
        Changed?.Invoke(this, EventArgs.Empty);
        if (playingChanged) PlayingChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyProfile(GameEntry game, string? exe)
    {
        var p = Store.Profile(game.Id);
        try
        {
            if (p.PowerPlan != PowerPlanChoice.Keep && _planOwner == null)
            {
                var available = PowerPlans.List().Select(x => x.Id).ToList();
                if (PowerPlans.Resolve(p.PowerPlan, available) is { } target && PowerPlans.Active() is { } current && current != target)
                {
                    _planBefore = current;
                    _planOwner = game.Id;
                    PowerPlans.SetActive(target);
                }
            }
            if (p.Priority != PriorityChoice.Normal && exe != null)
                ProcessWatcher.TrySetPriority(exe, p.Priority);
            // GPU preferida: grava assim que o executável é descoberto (vale a partir da próxima vez que o jogo abrir).
            if (p.Gpu != GpuChoice.Default && exe != null && GpuPreference.Get(exe) != p.Gpu)
                GpuPreference.Set(exe, p.Gpu);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void RestoreProfile(GameEntry game)
    {
        if (_planOwner != game.Id) return;
        try
        {
            if (_planBefore is { } plan) PowerPlans.SetActive(plan);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        _planOwner = null;
        _planBefore = null;
    }

    private void TrySave()
    {
        try { Store.Save(); }
        catch (Exception ex) { App.Log(ex); }
    }

    /// <summary>Fecha as sessões abertas e devolve o plano de energia original (ao sair do app).</summary>
    public void Shutdown()
    {
        _timer.Stop();
        try
        {
            var now = DateTimeOffset.Now;
            foreach (var e in _tracker.StopAll(now))
            {
                Store.Apply(e, now);
                if (e.Kind == TrackerEventKind.Ended) RestoreProfile(e.Game);
            }
            TrySave();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }
}
