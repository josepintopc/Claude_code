using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SupernovaBench.App.Controls;

/// <summary>true → Visible. Com ConverterParameter="invert", o contrário.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value is true;
        if (parameter as string == "invert") v = !v;
        return v ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible ^ (parameter as string == "invert");
}

/// <summary>Objeto/texto vazio → Collapsed. Com ConverterParameter="invert", o contrário.</summary>
public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool empty = value == null || (value is string s && string.IsNullOrWhiteSpace(s)) || (value is int i && i == 0);
        if (parameter as string == "invert") empty = !empty;
        return empty ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
