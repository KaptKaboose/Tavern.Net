using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>Visible when the bound int is non-zero — used to only show the counter badge on a card once it actually has one.</summary>
public sealed class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i != 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
