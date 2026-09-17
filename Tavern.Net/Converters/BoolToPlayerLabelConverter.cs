using System.Globalization;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>"You" for true, "Opp" for false — labels a merged online Play Log entry
/// (ViewModels.MergedLogEntry.IsOwn) by which player it belongs to.</summary>
public sealed class BoolToPlayerLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? "You" : "Opp";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
