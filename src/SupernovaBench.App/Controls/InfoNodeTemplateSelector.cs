using System.Windows;
using System.Windows.Controls;
using SupernovaBench.Core.Info;

namespace SupernovaBench.App.Controls;

/// <summary>Grupo (com filhos) vira cartão; folha vira linha "rótulo: valor". Resolve os templates por chave (permite recursão).</summary>
public sealed class InfoNodeTemplateSelector : DataTemplateSelector
{
    public string GroupTemplateKey { get; set; } = "InfoGroupTemplate";
    public string FieldTemplateKey { get; set; } = "InfoFieldTemplate";

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        if (item is not InfoNode node || container is not FrameworkElement fe) return base.SelectTemplate(item, container);
        return fe.TryFindResource(node.IsGroup ? GroupTemplateKey : FieldTemplateKey) as DataTemplate;
    }
}
