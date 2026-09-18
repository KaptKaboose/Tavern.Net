using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Tavern.Net.Converters;

/// <summary>Colors a sideboard-panel row's "xN" copy count so how many copies there are reads at a
/// glance: 1 white, 2 light blue, 3 light yellow, 4 light orange. Anything above 4 keeps 4's color —
/// technically illegal, but the panel reflects what was imported rather than policing the rules.</summary>
public sealed class CopyCountToBrushConverter : IValueConverter
{
    private static readonly Brush One = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
    private static readonly Brush Two = new SolidColorBrush(Color.FromRgb(0x90, 0xCD, 0xF4));
    private static readonly Brush Three = new SolidColorBrush(Color.FromRgb(0xFA, 0xF0, 0x89));
    private static readonly Brush FourOrMore = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));

    static CopyCountToBrushConverter()
    {
        One.Freeze();
        Two.Freeze();
        Three.Freeze();
        FourOrMore.Freeze();
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        int count when count >= 4 => FourOrMore,
        3 => Three,
        2 => Two,
        _ => One,
    };

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
