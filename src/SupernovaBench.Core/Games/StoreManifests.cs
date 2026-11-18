using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SupernovaBench.Core.Games;

/// <summary>Manifesto da Epic Games (C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests\*.item).</summary>
public static class EpicManifest
{
    public static GameEntry? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var r = doc.RootElement;
        string? S(string p) => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        bool B(string p) => r.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;

        if (B("bIsIncompleteInstall")) return null;
        string? app = S("AppName"), name = S("DisplayName"), dir = S("InstallLocation");
        if (string.IsNullOrWhiteSpace(app) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(dir)) return null;

        // DLC e complementos: não são jogos por si só.
        if (S("MainGameAppName") is { Length: > 0 } main && !string.Equals(main, app, StringComparison.OrdinalIgnoreCase)) return null;
        if (r.TryGetProperty("AppCategories", out var cats) && cats.ValueKind == JsonValueKind.Array)
        {
            var list = cats.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList();
            if (list.Count > 0 && (!list.Contains("games", StringComparer.OrdinalIgnoreCase) || list.Contains("addons", StringComparer.OrdinalIgnoreCase)))
                return null;
        }

        string? exe = S("LaunchExecutable") is { Length: > 0 } le ? Path.Combine(dir, le) : null;
        long? size = r.TryGetProperty("InstallSize", out var sz) && sz.TryGetInt64(out long n) && n > 0 ? n : null;
        return new GameEntry
        {
            Id = GameEntry.MakeId(GameStore.Epic, app),
            Name = name,
            Store = GameStore.Epic,
            InstallDir = dir,
            Executable = exe,
            LaunchUri = LaunchUri(S("CatalogNamespace"), S("CatalogItemId"), app),
            SizeBytes = size,
        };
    }

    public static string LaunchUri(string? ns, string? item, string app)
        => !string.IsNullOrEmpty(ns) && !string.IsNullOrEmpty(item)
            ? $"com.epicgames.launcher://apps/{ns}%3A{item}%3A{app}?action=launch&silent=true"
            : $"com.epicgames.launcher://apps/{app}?action=launch&silent=true";
}

/// <summary>Arquivo goggame-&lt;id&gt;.info que o GOG Galaxy grava na pasta do jogo.</summary>
public static class GogInfo
{
    public sealed record Info(string? GameId, string? Name, string? ExePath, string? Arguments, string? WorkingDir);

    public static Info? Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true });
        var r = doc.RootElement;
        string? S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        JsonElement? task = null;
        if (r.TryGetProperty("playTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array)
        {
            foreach (var t in tasks.EnumerateArray())
            {
                if (S(t, "type") is { } type && type != "FileTask") continue;
                bool primary = t.TryGetProperty("isPrimary", out var p) && p.ValueKind == JsonValueKind.True;
                if (primary) { task = t; break; }
                task ??= t;
            }
        }
        return new Info(S(r, "gameId"), S(r, "name"), task is { } tt ? S(tt, "path") : null,
                        task is { } ta ? S(ta, "arguments") : null, task is { } tw ? S(tw, "workingDir") : null);
    }
}

/// <summary>MicrosoftGame.config dos jogos do app Xbox / Game Pass.</summary>
public static class XboxGameConfig
{
    public sealed record Config(string IdentityName, string? DisplayName, string? Executable, string? AppId);

    public static Config? Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        XElement? E(XContainer c, string name) => c.Descendants().FirstOrDefault(e => e.Name.LocalName == name);
        string? A(XElement? e, string name) => e?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;

        string? identity = A(E(doc, "Identity"), "Name");
        if (string.IsNullOrWhiteSpace(identity)) return null;
        string? display = A(E(doc, "ShellVisuals"), "DefaultDisplayName");
        if (display != null && display.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) display = null;
        var exe = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Executable");
        return new Config(identity, display, A(exe, "Name"), A(exe, "Id"));
    }

    public static string LaunchUri(string packageFamilyName, string? appId) => $"shell:AppsFolder\\{packageFamilyName}!{appId ?? "Game"}";
}

/// <summary>Jogos da Blizzard: o desinstalador traz o "--uid", que vira o código de abertura do Battle.net.</summary>
public static class BattleNetCodes
{
    private static readonly Dictionary<string, string> Codes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["prometheus"] = "Pro", ["fenris"] = "Fen", ["diablo3"] = "D3", ["osi"] = "OSI", ["wow"] = "WoW",
        ["wow_classic"] = "WoWC", ["wow_classic_era"] = "WoWC", ["s2"] = "S2", ["s1"] = "S1", ["hs_beta"] = "WTCG",
        ["heroes"] = "Hero", ["w3"] = "W3", ["odin"] = "ODIN", ["lazarus"] = "LAZR", ["zeus"] = "ZEUS",
        ["fore"] = "FORE", ["viper"] = "VIPR", ["auks"] = "AUKS", ["anbs"] = "ANBS", ["rtro"] = "RTRO", ["wlby"] = "WLBY",
    };

    private static readonly Regex UidRegex = new(@"--uid=""?([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    public static string? ParseUid(string? uninstallString)
        => uninstallString != null && UidRegex.Match(uninstallString) is { Success: true } m ? m.Groups[1].Value : null;

    public static string? LaunchCode(string? uid) => uid != null && Codes.TryGetValue(uid, out var c) ? c : null;

    /// <summary>"battlenet" (o próprio launcher) e agentes não são jogos.</summary>
    public static bool IsLauncher(string? uid) => uid is null || uid.Equals("battle.net", StringComparison.OrdinalIgnoreCase)
                                                  || uid.Equals("bna", StringComparison.OrdinalIgnoreCase) || uid.Equals("agent", StringComparison.OrdinalIgnoreCase);
}
