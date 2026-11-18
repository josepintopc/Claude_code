namespace SupernovaBench.Core.Games;

/// <summary>Loja/launcher de onde o jogo veio.</summary>
public enum GameStore { Steam, Epic, Gog, Ubisoft, Ea, BattleNet, Xbox, Manual }

/// <summary>Um jogo instalado, encontrado num launcher ou adicionado à mão.</summary>
public sealed record GameEntry
{
    /// <summary>Identificador estável: "loja:id" (ex.: "steam:570", "epic:Fortnite", "manual:3f2a...").</summary>
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public GameStore Store { get; init; }
    public string InstallDir { get; init; } = "";

    /// <summary>Executável principal, quando conhecido (a Steam não informa: é descoberto na primeira partida).</summary>
    public string? Executable { get; init; }
    public string? Arguments { get; init; }

    /// <summary>Endereço que abre o jogo pelo launcher (steam://, uplay://...). Sem ele, abre pelo executável.</summary>
    public string? LaunchUri { get; init; }

    /// <summary>Programa que abre o jogo quando não é o próprio executável (ex.: Battle.net.exe --exec="launch Pro").</summary>
    public string? LaunchExecutable { get; init; }
    public string? LaunchArguments { get; init; }

    public long? SizeBytes { get; init; }

    /// <summary>App ID da Steam (para a capa); para outras lojas é descoberto pela busca da loja Steam.</summary>
    public int? SteamAppId { get; init; }

    /// <summary>Tempo de jogo e última partida informados pela própria loja (hoje só a Steam).</summary>
    public long? StorePlaytimeMinutes { get; init; }
    public DateTimeOffset? StoreLastPlayed { get; init; }

    public static string StoreKey(GameStore store) => store switch
    {
        GameStore.Steam => "steam",
        GameStore.Epic => "epic",
        GameStore.Gog => "gog",
        GameStore.Ubisoft => "ubisoft",
        GameStore.Ea => "ea",
        GameStore.BattleNet => "battlenet",
        GameStore.Xbox => "xbox",
        _ => "manual",
    };

    public static string StoreName(GameStore store) => store switch
    {
        GameStore.Steam => "Steam",
        GameStore.Epic => "Epic Games",
        GameStore.Gog => "GOG",
        GameStore.Ubisoft => "Ubisoft Connect",
        GameStore.Ea => "EA app",
        GameStore.BattleNet => "Battle.net",
        GameStore.Xbox => "Xbox",
        _ => "Manual",
    };

    public static string MakeId(GameStore store, string storeId) => StoreKey(store) + ":" + storeId;
}

/// <summary>Operações com caminhos do Windows que funcionam igual em qualquer sistema (aceitam "\" e "/").</summary>
public static class WinPath
{
    private static int LastSeparator(string p) => Math.Max(p.LastIndexOf('\\'), p.LastIndexOf('/'));

    public static string FileName(string path) => path[(LastSeparator(path) + 1)..];

    public static string FileNameWithoutExtension(string path)
    {
        string f = FileName(path);
        int dot = f.LastIndexOf('.');
        return dot > 0 ? f[..dot] : f;
    }

    public static string DirectoryName(string path)
    {
        int i = LastSeparator(path.TrimEnd('\\', '/'));
        return i > 0 ? path[..i] : "";
    }
}
