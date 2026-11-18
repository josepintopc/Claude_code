using System.Runtime.Versioning;
using Microsoft.Win32;
using SupernovaBench.Core.Games;

namespace SupernovaBench.Games;

/// <summary>Resultado da busca: jogos encontrados e, por loja, se o launcher existe e se houve erro.</summary>
public sealed class ScanResult
{
    public List<GameEntry> Games { get; } = new();
    public HashSet<GameStore> StoresFound { get; } = new();
    public Dictionary<GameStore, string> Errors { get; } = new();
}

/// <summary>Encontra os jogos instalados em cada launcher (registro + arquivos dos launchers). Cada loja falha sozinha.</summary>
[SupportedOSPlatform("windows")]
public static class GameScanner
{
    public static ScanResult ScanAll(IEnumerable<GameEntry>? manual = null)
    {
        var r = new ScanResult();
        void Run(GameStore store, Func<List<GameEntry>?> scan)
        {
            try
            {
                var games = scan();
                if (games == null) return;          // launcher não instalado
                r.StoresFound.Add(store);
                r.Games.AddRange(games);
            }
            catch (Exception ex)
            {
                r.Errors[store] = ex.Message;
            }
        }

        Run(GameStore.Steam, Steam);
        Run(GameStore.Epic, Epic);
        Run(GameStore.Gog, Gog);
        Run(GameStore.Ubisoft, Ubisoft);
        Run(GameStore.Ea, Ea);
        Run(GameStore.BattleNet, BattleNet);
        Run(GameStore.Xbox, Xbox);
        if (manual != null) r.Games.AddRange(manual);

        // Mesmo jogo achado por duas fontes (ex.: EA pela desinstalação e pela Steam): fica o primeiro.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<GameEntry>();
        foreach (var g in r.Games)
        {
            string key = PlaytimeTracker.NormalizeDir(g.InstallDir);
            if (g.Store != GameStore.Manual && key.Length > 0 && !seen.Add(key)) continue;
            unique.Add(g);
        }
        r.Games.Clear();
        r.Games.AddRange(unique.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase));
        return r;
    }

    // ------------------------------------------------------------------ Steam

    public static string? SteamRoot()
    {
        var path = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                   ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        if (string.IsNullOrWhiteSpace(path)) return null;
        path = path.Replace('/', '\\');
        return Directory.Exists(path) ? path : null;
    }

    private static List<GameEntry>? Steam() => SteamRoot() is { } root ? SteamLibrary.ScanFolder(root) : null;

    // ------------------------------------------------------------------ Epic

    private static List<GameEntry>? Epic()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(dir)) return null;
        var list = new List<GameEntry>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.item"))
        {
            try
            {
                if (EpicManifest.Parse(File.ReadAllText(file)) is { } g && Directory.Exists(g.InstallDir)) list.Add(g);
            }
            catch (Exception) { /* manifesto corrompido: pula */ }
        }
        return list;
    }

    // ------------------------------------------------------------------ GOG

    private static List<GameEntry>? Gog()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\Games");
        if (key == null) return null;
        var list = new List<GameEntry>();
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            if (g == null) continue;
            string? dir = g.GetValue("path") as string;
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            string name = g.GetValue("gameName") as string ?? id;
            string? exe = g.GetValue("exe") as string;
            string? args = g.GetValue("launchParam") as string;

            var info = Path.Combine(dir, $"goggame-{id}.info");
            if (File.Exists(info))
            {
                try
                {
                    if (GogInfo.Parse(File.ReadAllText(info)) is { } gi)
                    {
                        if (!string.IsNullOrWhiteSpace(gi.Name)) name = gi.Name!;
                        if (!string.IsNullOrWhiteSpace(gi.ExePath)) exe = Path.Combine(dir, gi.ExePath!);
                        if (!string.IsNullOrWhiteSpace(gi.Arguments)) args = gi.Arguments;
                    }
                }
                catch (Exception) { }
            }
            // DLCs do GOG têm "dependsOn" apontando para o jogo principal.
            if (g.GetValue("dependsOn") is string dep && !string.IsNullOrWhiteSpace(dep)) continue;
            list.Add(new GameEntry
            {
                Id = GameEntry.MakeId(GameStore.Gog, id),
                Name = name,
                Store = GameStore.Gog,
                InstallDir = dir,
                Executable = exe,
                Arguments = args,
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ Ubisoft Connect

    private static List<GameEntry>? Ubisoft()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher\Installs");
        if (key == null) return Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Ubisoft\Launcher") != null ? new List<GameEntry>() : null;
        var list = new List<GameEntry>();
        foreach (var id in key.GetSubKeyNames())
        {
            using var g = key.OpenSubKey(id);
            string? dir = (g?.GetValue("InstallDir") as string)?.Replace('/', '\\').TrimEnd('\\');
            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) continue;
            string name = UninstallValue($"Uplay Install {id}", "DisplayName") ?? Path.GetFileName(dir);
            list.Add(new GameEntry
            {
                Id = GameEntry.MakeId(GameStore.Ubisoft, id),
                Name = name,
                Store = GameStore.Ubisoft,
                InstallDir = dir,
                LaunchUri = $"uplay://launch/{id}/0",
            });
        }
        return list;
    }

    // ------------------------------------------------------------------ EA app

    private static List<GameEntry>? Ea()
    {
        bool hasLauncher = UninstallEntries().Any(e => e.Name is "EA app" or "Origin");
        var list = new List<GameEntry>();
        foreach (var e in UninstallEntries())
        {
            if (e.Publisher == null || !e.Publisher.Contains("Electronic Arts", StringComparison.OrdinalIgnoreCase)) continue;
            if (e.Name is null or "EA app" or "Origin" || e.Name.StartsWith("EA ", StringComparison.Ordinal) && e.Name.Contains("app", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrWhiteSpace(e.InstallLocation) || !Directory.Exists(e.InstallLocation)) continue;
            string? exe = ExeFromIcon(e.DisplayIcon);
            list.Add(new GameEntry
            {
                Id = GameEntry.MakeId(GameStore.Ea, e.Key),
                Name = e.Name,
                Store = GameStore.Ea,
                InstallDir = e.InstallLocation.TrimEnd('\\'),
                Executable = exe,
                SizeBytes = e.EstimatedSizeKb is > 0 ? e.EstimatedSizeKb * 1024 : null,
            });
        }
        return hasLauncher || list.Count > 0 ? list : null;
    }

    // ------------------------------------------------------------------ Battle.net

    private static List<GameEntry>? BattleNet()
    {
        var entries = UninstallEntries().ToList();
        var launcher = entries.FirstOrDefault(e => e.Name == "Battle.net");
        string? bnetExe = launcher?.InstallLocation is { } loc ? Path.Combine(loc, "Battle.net.exe") : null;
        if (bnetExe != null && !File.Exists(bnetExe)) bnetExe = null;

        var list = new List<GameEntry>();
        foreach (var e in entries)
        {
            if (e.UninstallString == null || !e.UninstallString.Contains("Blizzard Uninstaller", StringComparison.OrdinalIgnoreCase)) continue;
            string? uid = BattleNetCodes.ParseUid(e.UninstallString);
            if (BattleNetCodes.IsLauncher(uid) || e.Name == null) continue;
            if (string.IsNullOrWhiteSpace(e.InstallLocation) || !Directory.Exists(e.InstallLocation)) continue;
            string? code = BattleNetCodes.LaunchCode(uid);
            var entry = new GameEntry
            {
                Id = GameEntry.MakeId(GameStore.BattleNet, uid!),
                Name = e.Name,
                Store = GameStore.BattleNet,
                InstallDir = e.InstallLocation.TrimEnd('\\'),
                Executable = ExeFromIcon(e.DisplayIcon),
                SizeBytes = e.EstimatedSizeKb is > 0 ? e.EstimatedSizeKb * 1024 : null,
            };
            // Com o código conhecido, abre pelo Battle.net (necessário para logar); senão, pelo .exe.
            if (code != null && bnetExe != null) entry = entry with { LaunchExecutable = bnetExe, LaunchArguments = $"--exec=\"launch {code}\"" };
            list.Add(entry);
        }
        return launcher != null || list.Count > 0 ? list : null;
    }

    // ------------------------------------------------------------------ Xbox / Game Pass

    private static List<GameEntry>? Xbox()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady && Directory.Exists(Path.Combine(d.RootDirectory.FullName, "XboxGames")))
                    roots.Add(Path.Combine(d.RootDirectory.FullName, "XboxGames"));
            }
            catch (IOException) { }
        }
        using (var repo = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\GamingServices\PackageRepository\Root"))
        {
            if (repo != null)
                foreach (var sub in repo.GetSubKeyNames())
                {
                    using var k = repo.OpenSubKey(sub);
                    if (k?.GetValue("Root") is string r && Directory.Exists(r)) roots.Add(r);
                }
        }
        if (roots.Count == 0) return null;

        var families = PackageFamilies();
        var list = new List<GameEntry>();
        foreach (var root in roots)
        {
            foreach (var gameDir in Directory.EnumerateDirectories(root))
            {
                var config = Path.Combine(gameDir, "Content", "MicrosoftGame.config");
                if (!File.Exists(config)) continue;
                XboxGameConfig.Config? c;
                try { c = XboxGameConfig.Parse(File.ReadAllText(config)); }
                catch (Exception) { continue; }
                if (c == null) continue;
                string? family = families.FirstOrDefault(f => f.StartsWith(c.IdentityName + "_", StringComparison.OrdinalIgnoreCase));
                var content = Path.Combine(gameDir, "Content");
                list.Add(new GameEntry
                {
                    Id = GameEntry.MakeId(GameStore.Xbox, c.IdentityName),
                    Name = c.DisplayName ?? Path.GetFileName(gameDir),
                    Store = GameStore.Xbox,
                    InstallDir = content,
                    Executable = c.Executable != null ? Path.Combine(content, c.Executable) : null,
                    LaunchUri = family != null ? XboxGameConfig.LaunchUri(family, c.AppId) : null,
                });
            }
        }
        return list;
    }

    private static List<string> PackageFamilies()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Families");
        return k?.GetSubKeyNames().ToList() ?? new List<string>();
    }

    // ------------------------------------------------------------------ desinstalação (Painel de Controle)

    private sealed record UninstallEntry(string Key, string? Name, string? Publisher, string? InstallLocation, string? DisplayIcon,
                                         string? UninstallString, long? EstimatedSizeKb);

    private static IEnumerable<UninstallEntry> UninstallEntries()
    {
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var root = Registry.LocalMachine.OpenSubKey(path);
            if (root == null) continue;
            foreach (var name in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(name);
                if (k == null) continue;
                yield return new UninstallEntry(name, k.GetValue("DisplayName") as string, k.GetValue("Publisher") as string,
                                                k.GetValue("InstallLocation") as string, k.GetValue("DisplayIcon") as string,
                                                k.GetValue("UninstallString") as string,
                                                k.GetValue("EstimatedSize") is int kb ? kb : null);
            }
        }
    }

    private static string? UninstallValue(string key, string value)
    {
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
            if (Registry.LocalMachine.OpenSubKey($@"{path}\{key}")?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        return null;
    }

    /// <summary>"C:\Jogo\jogo.exe,0" → "C:\Jogo\jogo.exe" (só se for um .exe que existe).</summary>
    internal static string? ExeFromIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        string p = icon.Trim().Trim('"');
        int comma = p.LastIndexOf(',');
        if (comma > 2 && !p[comma..].Contains('\\')) p = p[..comma].Trim().Trim('"');
        return p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(p) ? p : null;
    }
}
