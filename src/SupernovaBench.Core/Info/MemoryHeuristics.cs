using System.Text.RegularExpressions;
using static SupernovaBench.Core.Localization.Loc;

namespace SupernovaBench.Core.Info;

/// <summary>
/// Heurísticas de memória que o Windows não informa diretamente: perfil EXPO/XMP e canais.
/// <para>Baseado no ResetExplode (MIT, © 2026 Gustavo / GustavoReset).</para>
/// </summary>
public static class MemoryHeuristics
{
    // Velocidades que costumam aparecer no part number do módulo (ex.: CMK32GX5M2B6000C30, F5-6000J3038F16GX2).
    private static readonly Regex SpeedInPart = new(
        @"(?<!\d)(2133|2400|2666|2933|3000|3200|3466|3600|3733|3800|4000|4133|4266|4400|4800|5200|5600|5800|6000|6200|6400|6600|6800|7000|7200|7400|7600|7800|8000|8200|8400|8800)(?!\d)",
        RegexOptions.Compiled);

    private static readonly Regex ChannelInLocator = new(
        @"(?i)(?:channel|chan|ch)[\s_-]*([A-H0-9])|dimm[\s_-]*([A-H])\d|(?:^|[^A-Za-z])([A-H])[12](?:$|[^0-9])",
        RegexOptions.Compiled);

    /// <summary>Maior velocidade (MT/s) escrita nos part numbers, ou 0.</summary>
    public static int SpeedFromPartNumbers(IEnumerable<string> partNumbers)
    {
        int best = 0;
        foreach (var p in partNumbers)
        {
            var m = SpeedInPart.Match(p ?? "");
            if (m.Success) best = Math.Max(best, int.Parse(m.Value));
        }
        return best;
    }

    /// <summary>Canal deduzido do nome do slot (ex.: "DIMM_A2" → A).</summary>
    public static string? ChannelOf(string? locator)
    {
        var m = ChannelInLocator.Match(locator ?? "");
        if (!m.Success) return null;
        for (int g = 1; g <= 3; g++) if (m.Groups[g].Success) return m.Groups[g].Value.ToUpperInvariant();
        return null;
    }

    /// <summary>Estima se EXPO/XMP está ligado comparando a velocidade em uso com a do part number.</summary>
    public static string XmpVerdict(int configured, int rated, int partNumberSpeed)
    {
        if (configured <= 0) return T("hw.mem.profile.unknownSpeed");
        if (partNumberSpeed > 0 && configured < partNumberSpeed * 0.95) return T("hw.mem.profile.off", configured, partNumberSpeed);
        if (partNumberSpeed > 0) return T("hw.mem.profile.on", configured, partNumberSpeed);
        if (rated > 0 && configured < rated) return T("hw.mem.profile.belowRated", configured, rated);
        return T("hw.mem.profile.cannotTell");
    }

    /// <summary>Deduz os canais pelos nomes dos slots (ex.: DIMM_A2 + DIMM_B2 = dois canais).</summary>
    public static string ChannelVerdict(IReadOnlyList<(string Locator, string Bank)> modules)
    {
        if (modules.Count == 1) return T("hw.mem.channels.single");
        var chans = modules.Select(m => ChannelOf(m.Locator) ?? ChannelOf(m.Bank)).ToList();
        if (chans.Count > 0 && chans.All(c => c != null))
        {
            int distinct = chans.Distinct().Count();
            return distinct >= 2 ? T("hw.mem.channels.multi", distinct) : T("hw.mem.channels.same");
        }
        return T("hw.mem.channels.unknown", string.Join(", ", modules.Select(m => m.Locator)));
    }
}
