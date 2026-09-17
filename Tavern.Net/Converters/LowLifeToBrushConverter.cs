using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Tavern.Net.Converters;

/// <summary>Tints a Life number red once it's at or below 5, darker still at exactly 0 — both your
/// own header Life and the opponent's, same thresholds either side.</summary>
public sealed class LowLifeToBrushConverter : IValueConverter
{
    private static readonly Brush ZeroBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x30, 0x30));
    private static readonly Brush LowBrush = new SolidColorBrush(Color.FromRgb(0xFC, 0x81, 0x81));
    private static readonly Brush NormalBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int life)
        {
            return NormalBrush;
        }

        if (life <= 0)
        {
            return ZeroBrush;
        }

        return life <= 5 ? LowBrush : NormalBrush;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
