using System.Runtime.Versioning;
using System.Text.Json;
using SupernovaBench.Core.Games;
using SupernovaBench.Core.Localization;
using SupernovaBench.Core.Storage;
using SupernovaBench.Games;
using SupernovaBench.Core.Extras;

namespace SupernovaBench.Cli;

/// <summary>"games list": jogos encontrados em cada launcher (mesma busca da página Biblioteca).</summary>
[SupportedOSPlatform("windows")]
internal static class GamesCommands
{
    public static int List(bool json)
    {
        var manual = new GameLibraryStore().ManualGames();
        var r = GameScanner.ScanAll(manual);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(r.Games, JsonFile.Options));
            return 0;
        }

        foreach (var store in Enum.GetValues<GameStore>().Where(s => s != GameStore.Manual))
        {
            var games = r.Games.Where(g => g.Store == store).ToList();
            string status = r.Errors.TryGetValue(store, out var err) ? Loc.T("common.error") + ": " + err
                          : r.StoresFound.Contains(store) ? games.Count.ToString(Loc.Culture) : Loc.T("games.cli.notFound");
            Console.WriteLine($"{GameEntry.StoreName(store)}: {status}");
            foreach (var g in games)
            {
                string time = g.StorePlaytimeMinutes is { } m ? $" · {m / 60.0:0.0} h" : "";
                Console.WriteLine($"  - {g.Name}{time}  [{g.InstallDir}]");
            }
        }
        var manualGames = r.Games.Where(g => g.Store == GameStore.Manual).ToList();
        if (manualGames.Count > 0)
        {
            Console.WriteLine($"{GameEntry.StoreName(GameStore.Manual)}: {manualGames.Count}");
            foreach (var g in manualGames) Console.WriteLine($"  - {g.Name}  [{g.Executable}]");
        }
        if (r.Games.Count == 0) Console.WriteLine(Loc.T("games.cli.none"));
        return 0;
    }

    /// <summary>"fps": FPS dos processos que estão entregando quadros (DirectX), uma linha por segundo.</summary>
    public static int Fps(int seconds)
    {
        using var monitor = new FrameRateMonitor();
        if (!monitor.Start())
        {
            Console.Error.WriteLine(monitor.Error == "admin" ? Loc.T("monitor.error.admin") : Loc.T("monitor.error.etw", monitor.Error ?? "?"));
            return 6;
        }
        for (int i = 0; i < seconds; i++)
        {
            Thread.Sleep(1000);
            double now = monitor.NowMs;
            if (monitor.Counter.Busiest(now) is { } pid && monitor.Counter.Snapshot(pid, now) is { } s)
                Console.WriteLine($"{ForegroundWindow.ProcessName(pid) ?? pid.ToString()}: {s.Fps:0} FPS · {s.FrameTimeMs:0.0} ms");
            else
                Console.WriteLine(Loc.T("monitor.waiting"));
        }
        return 0;
    }
}
