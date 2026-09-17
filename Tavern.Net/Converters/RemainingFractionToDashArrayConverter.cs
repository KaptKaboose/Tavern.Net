using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Tavern.Net.Converters;

/// <summary>Drives a depleting countdown ring: WPF's Shape.StrokeDashArray is in units of
/// StrokeThickness, not absolute pixels, and there's no built-in "percent drawn" stroke — so this
/// takes a 0-1 remaining fraction and a fixed circumference (in stroke-thickness units, passed as
/// ConverterParameter — see CountdownRingView, which computes it from its own Diameter/Thickness)
/// and returns {fraction * circumference, circumference}: one shrinking dash, then a gap big enough
/// that only that one dash ever paints.</summary>
public sealed class RemainingFractionToDashArrayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fraction = value is double d ? Math.Clamp(d, 0, 1) : 1.0;
        var circumference = System.Convert.ToDouble(parameter, culture);
        return new DoubleCollection { fraction * circumference, circumference };
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
