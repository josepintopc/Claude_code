using SupernovaBench.Core.Storage;

namespace SupernovaBench.Core.Games;

public enum PowerPlanChoice { Keep, Balanced, HighPerformance, Ultimate }

public enum PriorityChoice { Normal, AboveNormal, High }

public enum GpuChoice { Default, PowerSaving, HighPerformance }

/// <summary>Perfil de desempenho de um jogo: aplicado quando ele abre, desfeito quando fecha.</summary>
public sealed record GameProfile
{
    public PowerPlanChoice PowerPlan { get; init; }
    public PriorityChoice Priority { get; init; }
    public GpuChoice Gpu { get; init; }

    public bool IsDefault => PowerPlan == PowerPlanChoice.Keep && Priority == PriorityChoice.Normal && Gpu == GpuChoice.Default;
}

/// <summary>Tempo de jogo e dados que o Supernova descobriu sobre um jogo.</summary>
public sealed class GameStats
{
    public double Minutes { get; set; }
    public int Sessions { get; set; }
    public DateTimeOffset? LastPlayed { get; set; }

    /// <summary>Executável visto rodando (descobre o .exe dos jogos da Steam).</summary>
    public string? Exe { get; set; }
}

/// <summary>Jogo adicionado à mão (um .exe qualquer).</summary>
public sealed class ManualGame
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Executable { get; set; } = "";
    public string? Arguments { get; set; }
}

public sealed class GameLibraryData
{
    public List<ManualGame> Manual { get; set; } = new();
    public HashSet<string> Hidden { get; set; } = new();
    public HashSet<string> Favorites { get; set; } = new();
    public Dictionary<string, GameProfile> Profiles { get; set; } = new();
    public Dictionary<string, GameStats> Stats { get; set; } = new();
}

/// <summary>games.json: jogos manuais, ocultos, favoritos, perfis e tempo de jogo. Thread-safe.</summary>
public sealed class GameLibraryStore
{
    private readonly string _path;
    private readonly object _lock = new();
    private readonly GameLibraryData _data;

    public GameLibraryStore(string? path = null)
    {
        _path = path ?? AppPaths.GamesFile;
        _data = JsonFile.Load<GameLibraryData>(_path) ?? new GameLibraryData();
        _data.Manual ??= new();
        _data.Hidden ??= new();
        _data.Favorites ??= new();
        _data.Profiles ??= new();
        _data.Stats ??= new();
    }

    public void Save()
    {
        lock (_lock) JsonFile.Save(_path, _data);
    }

    public IReadOnlyList<GameEntry> ManualGames()
    {
        lock (_lock)
            return _data.Manual.Select(ToEntry).ToList();
    }

    public static GameEntry ToEntry(ManualGame m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        Store = GameStore.Manual,
        InstallDir = WinPath.DirectoryName(m.Executable),
        Executable = m.Executable,
        Arguments = m.Arguments,
    };

    public GameEntry AddManual(string name, string executable, string? arguments = null)
    {
        var m = new ManualGame
        {
            Id = GameEntry.MakeId(GameStore.Manual, Guid.NewGuid().ToString("N")[..12]),
            Name = string.IsNullOrWhiteSpace(name) ? WinPath.FileNameWithoutExtension(executable) : name.Trim(),
            Executable = executable,
            Arguments = arguments,
        };
        lock (_lock) _data.Manual.Add(m);
        return ToEntry(m);
    }

    public bool RemoveManual(string id)
    {
        lock (_lock) return _data.Manual.RemoveAll(m => m.Id == id) > 0;
    }

    public bool IsHidden(string id) { lock (_lock) return _data.Hidden.Contains(id); }
    public bool IsFavorite(string id) { lock (_lock) return _data.Favorites.Contains(id); }

    public void SetHidden(string id, bool hidden)
    {
        lock (_lock) { if (hidden) _data.Hidden.Add(id); else _data.Hidden.Remove(id); }
    }

    public void SetFavorite(string id, bool favorite)
    {
        lock (_lock) { if (favorite) _data.Favorites.Add(id); else _data.Favorites.Remove(id); }
    }

    public GameProfile Profile(string id)
    {
        lock (_lock) return _data.Profiles.TryGetValue(id, out var p) ? p : new GameProfile();
    }

    public void SetProfile(string id, GameProfile profile)
    {
        lock (_lock)
        {
            if (profile.IsDefault) _data.Profiles.Remove(id);
            else _data.Profiles[id] = profile;
        }
    }

    public GameStats? Stats(string id)
    {
        lock (_lock) return _data.Stats.TryGetValue(id, out var s) ? s : null;
    }

    /// <summary>Aplica um evento do rastreador (soma minutos, conta sessão, anota o executável).</summary>
    public void Apply(TrackerEvent e, DateTimeOffset now)
    {
        lock (_lock)
        {
            if (!_data.Stats.TryGetValue(e.Game.Id, out var s)) _data.Stats[e.Game.Id] = s = new GameStats();
            if (e.Exe != null) s.Exe = e.Exe;
            if (e.Kind == TrackerEventKind.Started) return;
            s.Minutes += Math.Max(0, e.Minutes);
            if (e.NewSession) s.Sessions++;
            if (e.Minutes > 0 || e.NewSession) s.LastPlayed = now;
        }
    }
}
