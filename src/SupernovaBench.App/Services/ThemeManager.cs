using System.Windows;

namespace SupernovaBench.App.Services;

/// <summary>Troca o dicionário de cores (Cosmic.xaml / Light.xaml) em tempo de execução.</summary>
public static class ThemeManager
{
    public const string Cosmic = "cosmic";
    public const string Light = "light";

    public static string Current { get; private set; } = Cosmic;

    public static event EventHandler? ThemeChanged;

    public static void Apply(string? theme)
    {
        theme = theme == Light ? Light : Cosmic;
        var file = theme == Light ? "Light.xaml" : "Cosmic.xaml";
        var dicts = Application.Current.Resources.MergedDictionaries;
        var fresh = new ResourceDictionary { Source = new Uri($"pack://application:,,,/SupernovaBench;component/Themes/{file}", UriKind.Absolute) };
        var existing = dicts.FirstOrDefault(d => d.Source != null &&
            (d.Source.OriginalString.EndsWith("Cosmic.xaml", StringComparison.OrdinalIgnoreCase) ||
             d.Source.OriginalString.EndsWith("Light.xaml", StringComparison.OrdinalIgnoreCase)));
        if (existing != null) dicts[dicts.IndexOf(existing)] = fresh;
        else dicts.Insert(0, fresh);
        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }
}
