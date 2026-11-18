using System.Globalization;

namespace SupernovaBench.Core.Games;

/// <summary>Um appmanifest_*.acf lido.</summary>
public sealed record SteamAppManifest(int AppId, string Name, string InstallDir, long SizeOnDisk, DateTimeOffset? LastUpdated);

/// <summary>Leitura da biblioteca da Steam a partir dos arquivos dela (sem API, sem login).</summary>
public static class SteamLibrary
{
    /// <summary>Apps que a Steam instala como "jogos" mas são ferramentas (redistribuíveis, Proton, SDKs...).</summary>
    private static readonly HashSet<int> ToolIds = new() { 228980, 1070560, 1391110, 1628350, 1493710, 2180100, 1826330, 961940, 1161040, 1245040, 1420170, 1580130, 1887720, 2348590, 2805730 };

    private static readonly string[] ToolNameParts =
    {
        "Steamworks Common", "Redistributable", "Proton ", "Proton-", "Steam Linux Runtime", "SteamVR", " SDK", "Dedicated Server", "Soundtrack",
    };

    public static bool IsTool(int appId, string name)
        => ToolIds.Contains(appId) || name.StartsWith("Proton", StringComparison.OrdinalIgnoreCase)
           || ToolNameParts.Any(p => name.Contains(p, StringComparison.OrdinalIgnoreCase));

    /// <summary>Pastas das bibliotecas (formato novo com "path" e formato antigo com o caminho direto).</summary>
    public static List<string> ParseLibraryFolders(string vdf)
    {
        var root = Vdf.Parse(vdf);
        var lf = root["libraryfolders"] ?? root["LibraryFolders"];
        var list = new List<string>();
        if (lf == null) return list;
        foreach (var (key, node) in lf.Children)
        {
            if (!int.TryParse(key, out _)) continue;
            string? path = node.IsObject ? node.Get("path") : node.Value;
            if (!string.IsNullOrWhiteSpace(path)) list.Add(path);
        }
        return list;
    }

    public static SteamAppManifest? ParseAppManifest(string acf)
    {
        var app = Vdf.Parse(acf)["AppState"];
        if (app == null || !int.TryParse(app.Get("appid"), out int id)) return null;
        string name = app.Get("name") ?? "";
        string dir = app.Get("installdir") ?? "";
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) return null;
        long.TryParse(app.Get("SizeOnDisk"), out long size);
        DateTimeOffset? updated = long.TryParse(app.Get("LastUpdated"), out long ts) && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts) : null;
        return new SteamAppManifest(id, name, dir, size, updated);
    }

    /// <summary>Tempo de jogo (minutos) e última partida por app, do localconfig.vdf de um usuário.</summary>
    public static Dictionary<int, (long Minutes, DateTimeOffset? LastPlayed)> ParsePlaytime(string localConfigVdf)
    {
        var result = new Dictionary<int, (long, DateTimeOffset?)>();
        var apps = Vdf.Parse(localConfigVdf).At("UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
        if (apps == null) return result;
        foreach (var (key, node) in apps.Children)
        {
            if (!int.TryParse(key, out int id) || !node.IsObject) continue;
            long.TryParse(node.Get("Playtime"), NumberStyles.Integer, CultureInfo.InvariantCulture, out long minutes);
            DateTimeOffset? last = long.TryParse(node.Get("LastPlayed"), out long ts) && ts > 0 ? DateTimeOffset.FromUnixTimeSeconds(ts) : null;
            if (minutes > 0 || last != null) result[id] = (minutes, last);
        }
        return result;
    }

    /// <summary>localconfig.vdf mais recente entre os usuários que já entraram neste PC.</summary>
    public static string? FindLocalConfig(string steamRoot)
    {
        var userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata)) return null;
        return Directory.EnumerateDirectories(userdata)
                        .Select(d => Path.Combine(d, "config", "localconfig.vdf"))
                        .Where(File.Exists)
                        .OrderByDescending(File.GetLastWriteTimeUtc)
                        .FirstOrDefault();
    }

    /// <summary>Todos os jogos instalados a partir da pasta da Steam (todas as bibliotecas).</summary>
    public static List<GameEntry> ScanFolder(string steamRoot)
    {
        var libraries = new List<string> { steamRoot };
        foreach (var candidate in new[] { Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), Path.Combine(steamRoot, "config", "libraryfolders.vdf") })
        {
            if (!File.Exists(candidate)) continue;
            try { libraries.AddRange(ParseLibraryFolders(File.ReadAllText(candidate))); }
            catch (IOException) { }
            break;
        }

        Dictionary<int, (long Minutes, DateTimeOffset? LastPlayed)> playtime = new();
        try
        {
            if (FindLocalConfig(steamRoot) is { } lc) playtime = ParsePlaytime(File.ReadAllText(lc));
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }

        var games = new Dictionary<int, GameEntry>();
        foreach (var lib in libraries.Select(NormalizeDir).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var apps = Path.Combine(lib, "steamapps");
            if (!Directory.Exists(apps)) continue;
            foreach (var acf in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
            {
                SteamAppManifest? m;
                try { m = ParseAppManifest(File.ReadAllText(acf)); }
                catch (IOException) { continue; }
                if (m == null || IsTool(m.AppId, m.Name) || games.ContainsKey(m.AppId)) continue;
                var dir = Path.Combine(apps, "common", m.InstallDir);
                if (!Directory.Exists(dir)) continue;
                playtime.TryGetValue(m.AppId, out var pt);
                games[m.AppId] = new GameEntry
                {
                    Id = GameEntry.MakeId(GameStore.Steam, m.AppId.ToString(CultureInfo.InvariantCulture)),
                    Name = m.Name,
                    Store = GameStore.Steam,
                    InstallDir = dir,
                    LaunchUri = $"steam://rungameid/{m.AppId}",
                    SizeBytes = m.SizeOnDisk > 0 ? m.SizeOnDisk : null,
                    SteamAppId = m.AppId,
                    StorePlaytimeMinutes = pt.Minutes > 0 ? pt.Minutes : null,
                    StoreLastPlayed = pt.LastPlayed,
                };
            }
        }
        return games.Values.ToList();
    }

    private static string NormalizeDir(string p) => p.Replace("\\\\", "\\").TrimEnd('\\', '/');
}
