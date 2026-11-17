using System.Globalization;
using System.Text.Json;

namespace SupernovaBench.Core.Localization;

public sealed record LanguageInfo(string Code, string NativeName);

/// <summary>
/// Traduções do app. As tabelas ficam em Localization/Strings/&lt;código&gt;.json (recursos embutidos).
/// Se uma chave faltar no idioma atual, usa o português; se faltar nos dois, mostra a própria chave.
/// </summary>
public static class Loc
{
    public const string DefaultLanguage = "pt-BR";

    public static IReadOnlyList<LanguageInfo> Languages { get; } = new[]
    {
        new LanguageInfo("pt-BR", "Português (Brasil)"),
        new LanguageInfo("en", "English"),
    };

    private static readonly Dictionary<string, IReadOnlyDictionary<string, string>> Cache = new();
    private static IReadOnlyDictionary<string, string> _current = Table(DefaultLanguage);
    private static IReadOnlyDictionary<string, string> _fallback = Table(DefaultLanguage);

    public static string Current { get; private set; } = DefaultLanguage;
    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo(DefaultLanguage);

    /// <summary>Disparado depois de trocar o idioma (a interface reavalia os textos).</summary>
    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? code)
    {
        var lang = Languages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))?.Code
                   ?? DefaultLanguage;
        Current = lang;
        Culture = CultureInfo.GetCultureInfo(lang);
        _current = Table(lang);
        _fallback = Table(DefaultLanguage);
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string T(string key)
        => _current.TryGetValue(key, out var v) ? v : _fallback.TryGetValue(key, out var f) ? f : key;

    public static string T(string key, params object?[] args)
    {
        try { return string.Format(Culture, T(key), args); }
        catch (FormatException) { return T(key); }
    }

    /// <summary>Tabela completa de um idioma (lida do recurso embutido e mantida em cache).</summary>
    public static IReadOnlyDictionary<string, string> Table(string code)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(code, out var t)) return t;
            var asm = typeof(Loc).Assembly;
            using var stream = asm.GetManifestResourceStream($"SupernovaBench.Strings.{code}.json");
            var dict = stream == null
                ? new Dictionary<string, string>()
                : JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
            Cache[code] = dict;
            return dict;
        }
    }
}
