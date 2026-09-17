using System.Globalization;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>"✓" for true, "✗" for false — used with a Foreground DataTrigger (green/red) for a
/// Ready indicator, rather than showing the raw boolean text.</summary>
public sealed class BoolToCheckMarkConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "✓" : "✗";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
