using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tavern.Net.Decklists;

namespace Tavern.Net.Converters;

public sealed class ResolutionStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ResolutionStatus.Matched => Brushes.MediumSeaGreen,
            ResolutionStatus.Ambiguous => Brushes.Goldenrod,
            ResolutionStatus.NotFound => Brushes.IndianRed,
            _ => Brushes.Gray,
        };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
