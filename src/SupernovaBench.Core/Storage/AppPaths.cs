namespace SupernovaBench.Core.Storage;

/// <summary>Pastas de dados do usuário (%AppData%\SupernovaBench no Windows).</summary>
public static class AppPaths
{
    private static string? _overrideRoot;

    public static string Root
    {
        get
        {
            var root = _overrideRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SupernovaBench");
            Directory.CreateDirectory(root);
            return root;
        }
    }

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");
    public static string LogFile => Path.Combine(Root, "supernova.log");
    public static string GamesFile => Path.Combine(Root, "games.json");

    /// <summary>Capas baixadas (cache).</summary>
    public static string CoversDir
    {
        get
        {
            var dir = Path.Combine(Root, "covers");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Usado pelos testes para não mexer na pasta real do usuário.</summary>
    public static void OverrideRoot(string? path) => _overrideRoot = path;
}
