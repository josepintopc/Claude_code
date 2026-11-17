using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using SupernovaBench.Core.Localization;

namespace SupernovaBench.App.Localization;

/// <summary>Fonte de binding das traduções; avisa a interface quando o idioma muda.</summary>
public sealed class LocProxy : INotifyPropertyChanged
{
    public static LocProxy Instance { get; } = new();

    private LocProxy()
    {
        Loc.LanguageChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public string this[string key] => Loc.T(key);

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Texto traduzido no XAML: <c>Text="{loc:Tr nav.overview}"</c>. Atualiza sozinho ao trocar de idioma.
/// </summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]") { Source = LocProxy.Instance, Mode = BindingMode.OneWay };
        return binding.ProvideValue(serviceProvider);
    }
}
