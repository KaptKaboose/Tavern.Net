using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>Visible when the bound int is non-zero (or, with "Invert" as the parameter, when it IS
/// zero) — used to only show the counter badge on a card once it actually has one, and for empty-list
/// hints keyed off a collection's Count.</summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isNonZero = value is int i && i != 0;
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var visible = invert ? !isNonZero : isNonZero;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
