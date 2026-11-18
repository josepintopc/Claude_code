using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SupernovaBench.Core.Games;

/// <summary>
/// Endereços das capas no CDN da Steam e escolha do resultado da busca da loja Steam pelo nome do jogo
/// (assim jogos de outras lojas que também estão na Steam ganham capa). Só monta e interpreta; quem baixa é o app.
/// </summary>
public static class CoverSearch
{
    private const string Cdn = "https://cdn.cloudflare.steamstatic.com/steam/apps/";

    /// <summary>Capa vertical 600×900 (a da biblioteca) e, se não existir, o banner horizontal.</summary>
    public static IReadOnlyList<string> SteamCoverUrls(int appId) => new[]
    {
        $"{Cdn}{appId}/library_600x900_2x.jpg",
        $"{Cdn}{appId}/library_600x900.jpg",
        $"{Cdn}{appId}/header.jpg",
    };

    public static string StoreSearchUrl(string name)
        => "https://store.steampowered.com/api/storesearch/?term=" + Uri.EscapeDataString(Normalize(name)) + "&l=english&cc=US";

    private static readonly string[] EditionWords =
    {
        "game of the year edition", "goty edition", "definitive edition", "deluxe edition", "standard edition", "complete edition",
        "ultimate edition", "gold edition", "enhanced edition", "anniversary edition", "edition", "goty",
    };

    /// <summary>Nome comparável: minúsculas, sem acentos, ™/®, pontuação e palavras de edição.</summary>
    public static string Normalize(string name)
    {
        var sb = new StringBuilder();
        foreach (char ch in name.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (ch is '™' or '®' or '©') continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }
        string s = " " + string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";
        foreach (var w in EditionWords) s = s.Replace(" " + w + " ", " ");
        return string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>App ID do resultado com nome equivalente (ou null, para não pôr capa errada).</summary>
    public static int? PickBestMatch(string json, string name)
    {
        string target = Normalize(name);
        if (target.Length == 0) return null;
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return null;

        int? prefix = null;
        foreach (var it in items.EnumerateArray())
        {
            if (it.TryGetProperty("type", out var t) && t.GetString() is { } type && type != "app") continue;
            if (!it.TryGetProperty("id", out var idEl) || !idEl.TryGetInt32(out int id)) continue;
            string candidate = Normalize(it.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
            if (candidate == target) return id;
            // "Cyberpunk 2077" x "Cyberpunk 2077 Phantom Liberty": só aceita se quase igual.
            if (prefix == null && (candidate.StartsWith(target + " ", StringComparison.Ordinal) || target.StartsWith(candidate + " ", StringComparison.Ordinal))
                && Math.Min(candidate.Length, target.Length) >= 0.8 * Math.Max(candidate.Length, target.Length))
                prefix = id;
        }
        return prefix;
    }
}
