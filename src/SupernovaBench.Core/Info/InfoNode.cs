using System.Text;

namespace SupernovaBench.Core.Info;

/// <summary>Nó da árvore de informações do sistema (rótulo: valor, com filhos).</summary>
public sealed class InfoNode
{
    public string Label { get; set; } = "";
    public string Value { get; set; } = "";

    /// <summary>Dado pessoal (número de série, nome do PC). Fica de fora dos relatórios exportados.</summary>
    public bool Sensitive { get; set; }

    public List<InfoNode> Children { get; set; } = new();

    public bool IsGroup => Children.Count > 0;
    public bool HasValue => !string.IsNullOrEmpty(Value);

    public InfoNode Add(string label, string? value = "", bool sensitive = false)
    {
        var n = new InfoNode { Label = label, Value = value ?? "", Sensitive = sensitive };
        Children.Add(n);
        return n;
    }

    /// <summary>Cópia sem os nós sensíveis.</summary>
    public InfoNode? Redacted()
    {
        if (Sensitive) return null;
        return new InfoNode
        {
            Label = Label,
            Value = Value,
            Children = Children.Select(c => c.Redacted()).Where(c => c != null).Select(c => c!).ToList(),
        };
    }

    public void AppendText(StringBuilder sb, int indent = 0)
    {
        sb.Append(' ', indent * 2).Append(Label);
        if (HasValue) sb.Append(": ").Append(Value);
        sb.AppendLine();
        foreach (var c in Children) c.AppendText(sb, indent + 1);
    }
}

/// <summary>Uma seção (aba) de informações: Sistema, CPU, Placa-mãe, Memória, GPU, Armazenamento.</summary>
public sealed class InfoSection
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public List<InfoNode> Nodes { get; set; } = new();

    public InfoSection Redacted() => new()
    {
        Id = Id,
        Title = Title,
        Nodes = Nodes.Select(n => n.Redacted()).Where(n => n != null).Select(n => n!).ToList(),
    };

    public string ToText(bool includeSensitive = true)
    {
        var src = includeSensitive ? this : Redacted();
        var sb = new StringBuilder();
        sb.AppendLine(src.Title.ToUpperInvariant());
        foreach (var n in src.Nodes) n.AppendText(sb, 1);
        return sb.ToString();
    }
}
