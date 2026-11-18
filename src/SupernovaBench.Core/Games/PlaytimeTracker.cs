namespace SupernovaBench.Core.Games;

public enum TrackerEventKind { Started, Progress, Ended }

/// <summary>
/// Evento do rastreador. <see cref="Minutes"/> é o tempo a somar ao total (desde o último evento);
/// <see cref="NewSession"/> indica que é a primeira soma desta sessão (conta +1 sessão).
/// </summary>
public sealed record TrackerEvent(TrackerEventKind Kind, GameEntry Game, string? Exe, double Minutes, bool NewSession, TimeSpan SessionLength);

/// <summary>
/// Conta o tempo de jogo olhando os processos abertos: um jogo está aberto quando algum executável roda de dentro
/// da pasta dele. Lógica pura (recebe a lista de caminhos e o horário), para poder ser testada sem Windows.
/// </summary>
public sealed class PlaytimeTracker
{
    private sealed class Session
    {
        public required GameEntry Game;
        public DateTimeOffset Start;
        public DateTimeOffset LastCommit;
        public bool Committed;
        public string? Exe;
    }

    private readonly TimeSpan _minimum;
    private readonly TimeSpan _checkpoint;
    private List<(string Dir, GameEntry Game)> _dirs = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public PlaytimeTracker(TimeSpan? minimumSession = null, TimeSpan? checkpoint = null)
    {
        _minimum = minimumSession ?? TimeSpan.FromMinutes(1);
        _checkpoint = checkpoint ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>Jogos abertos agora e o início da sessão de cada um.</summary>
    public IReadOnlyList<(GameEntry Game, DateTimeOffset Start, string? Exe)> Running
        => _sessions.Values.Select(s => (s.Game, s.Start, s.Exe)).ToList();

    public void SetGames(IEnumerable<GameEntry> games)
    {
        // Pastas mais longas primeiro: se uma pasta contém outra, vale a mais específica.
        _dirs = games.Where(g => NormalizeDir(g.InstallDir).Length >= 6)
                     .Select(g => (NormalizeDir(g.InstallDir) + "\\", g))
                     .OrderByDescending(x => x.Item1.Length)
                     .ToList();
    }

    public static string NormalizeDir(string path) => path.Replace('/', '\\').TrimEnd('\\').ToLowerInvariant();

    /// <summary>Atualiza com os executáveis abertos agora. Devolve os eventos de início, progresso e fim.</summary>
    public List<TrackerEvent> Update(IEnumerable<string> runningExes, DateTimeOffset now)
    {
        var events = new List<TrackerEvent>();
        var found = new Dictionary<string, (GameEntry Game, List<string> Exes)>(StringComparer.OrdinalIgnoreCase);
        foreach (var exe in runningExes)
        {
            if (string.IsNullOrEmpty(exe)) continue;
            string p = exe.Replace('/', '\\').ToLowerInvariant();
            foreach (var (dir, game) in _dirs)
            {
                if (!p.StartsWith(dir, StringComparison.Ordinal)) continue;
                if (!found.TryGetValue(game.Id, out var entry)) found[game.Id] = entry = (game, new List<string>());
                entry.Exes.Add(exe);
                break;
            }
        }

        foreach (var (id, (game, exes)) in found)
        {
            string? exe = PickExe(exes);
            if (!_sessions.TryGetValue(id, out var s))
            {
                _sessions[id] = s = new Session { Game = game, Start = now, LastCommit = now, Exe = exe };
                events.Add(new TrackerEvent(TrackerEventKind.Started, game, exe, 0, false, TimeSpan.Zero));
                continue;
            }
            if (exe != null && (s.Exe == null || IsHelper(s.Exe))) s.Exe = exe;
            // Grava aos poucos (se o PC desligar no meio, o tempo até o último ponto não se perde).
            if (now - s.Start >= _minimum && now - s.LastCommit >= _checkpoint)
            {
                events.Add(new TrackerEvent(TrackerEventKind.Progress, game, s.Exe, (now - s.LastCommit).TotalMinutes, !s.Committed, now - s.Start));
                s.LastCommit = now;
                s.Committed = true;
            }
        }

        foreach (var id in _sessions.Keys.Where(k => !found.ContainsKey(k)).ToList())
        {
            var s = _sessions[id];
            _sessions.Remove(id);
            var length = now - s.Start;
            bool counted = length >= _minimum;
            double minutes = counted ? (now - s.LastCommit).TotalMinutes : 0;
            events.Add(new TrackerEvent(TrackerEventKind.Ended, s.Game, s.Exe, minutes, counted && !s.Committed, length));
        }
        return events;
    }

    /// <summary>Encerra todas as sessões (ao fechar o app).</summary>
    public List<TrackerEvent> StopAll(DateTimeOffset now) => Update(Array.Empty<string>(), now);

    private static readonly string[] HelperWords = { "crash", "launcher", "helper", "updater", "unins", "redist", "setup", "report", "easyanticheat", "battleye", "overlay" };

    private static bool IsHelper(string exe)
    {
        string f = WinPath.FileName(exe).ToLowerInvariant();
        return HelperWords.Any(f.Contains);
    }

    /// <summary>Entre vários executáveis do jogo, prefere o que não parece launcher/crash handler.</summary>
    private static string? PickExe(List<string> exes) => exes.FirstOrDefault(e => !IsHelper(e)) ?? exes.FirstOrDefault();
}
