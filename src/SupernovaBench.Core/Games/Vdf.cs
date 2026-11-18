using System.Text;

namespace SupernovaBench.Core.Games;

/// <summary>Nó de um arquivo KeyValues da Valve (VDF/ACF): tem um valor de texto ou filhos.</summary>
public sealed class VdfNode
{
    private readonly Dictionary<string, VdfNode> _children = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<KeyValuePair<string, VdfNode>> _ordered = new();

    public string? Value { get; init; }
    public bool IsObject => Value == null;

    /// <summary>Filhos na ordem do arquivo.</summary>
    public IReadOnlyList<KeyValuePair<string, VdfNode>> Children => _ordered;

    public VdfNode? this[string key] => _children.TryGetValue(key, out var n) ? n : null;

    /// <summary>Segue um caminho de chaves (sem diferenciar maiúsculas).</summary>
    public VdfNode? At(params string[] path)
    {
        VdfNode? n = this;
        foreach (var k in path)
        {
            n = n?[k];
            if (n == null) return null;
        }
        return n;
    }

    public string? Get(string key) => this[key]?.Value;

    internal void Add(string key, VdfNode node)
    {
        _children[key] = node;
        _ordered.Add(new(key, node));
    }
}

/// <summary>Leitor de KeyValues em texto (libraryfolders.vdf, appmanifest_*.acf, localconfig.vdf).</summary>
public static class Vdf
{
    public static VdfNode Parse(string text)
    {
        int pos = 0;
        var root = new VdfNode();
        ParseObject(text, ref pos, root, topLevel: true);
        return root;
    }

    private static void ParseObject(string s, ref int pos, VdfNode target, bool topLevel)
    {
        while (true)
        {
            var key = NextToken(s, ref pos, out bool keyQuoted);
            if (key == null) return;                 // fim do arquivo
            if (!keyQuoted && key == "}")
            {
                if (topLevel) continue;              // chave sobrando: ignora
                return;
            }
            if (!keyQuoted && key == "{") continue;  // abre sem chave: ignora

            var value = NextToken(s, ref pos, out bool valueQuoted);
            if (value == null) return;
            if (!valueQuoted && value == "{")
            {
                var child = new VdfNode();
                ParseObject(s, ref pos, child, topLevel: false);
                target.Add(key, child);
            }
            else if (!valueQuoted && value == "}")
            {
                // chave sem valor antes do fecha: termina o objeto
                return;
            }
            else
            {
                target.Add(key, new VdfNode { Value = value });
            }
            SkipCondition(s, ref pos);
        }
    }

    /// <summary>Pula condições como [$WIN32] depois de um valor.</summary>
    private static void SkipCondition(string s, ref int pos)
    {
        int p = pos;
        SkipSpaceAndComments(s, ref p);
        if (p < s.Length && s[p] == '[')
        {
            int end = s.IndexOf(']', p);
            pos = end < 0 ? s.Length : end + 1;
        }
    }

    private static void SkipSpaceAndComments(string s, ref int pos)
    {
        while (pos < s.Length)
        {
            if (char.IsWhiteSpace(s[pos])) pos++;
            else if (s[pos] == '/' && pos + 1 < s.Length && s[pos + 1] == '/')
            {
                int nl = s.IndexOf('\n', pos);
                pos = nl < 0 ? s.Length : nl + 1;
            }
            else break;
        }
    }

    private static string? NextToken(string s, ref int pos, out bool quoted)
    {
        quoted = false;
        SkipSpaceAndComments(s, ref pos);
        if (pos >= s.Length) return null;
        char c = s[pos];
        if (c == '{' || c == '}')
        {
            pos++;
            return c.ToString();
        }
        if (c == '"')
        {
            quoted = true;
            pos++;
            var sb = new StringBuilder();
            while (pos < s.Length && s[pos] != '"')
            {
                if (s[pos] == '\\' && pos + 1 < s.Length)
                {
                    char e = s[pos + 1];
                    sb.Append(e switch { 'n' => '\n', 't' => '\t', '\\' => '\\', '"' => '"', _ => e });
                    pos += 2;
                    continue;
                }
                sb.Append(s[pos++]);
            }
            pos++; // aspas finais
            return sb.ToString();
        }
        int start = pos;
        while (pos < s.Length && !char.IsWhiteSpace(s[pos]) && s[pos] != '{' && s[pos] != '}' && s[pos] != '"') pos++;
        return s[start..pos];
    }
}
