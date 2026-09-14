using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tavern.Net.Game;

namespace Tavern.Net.Converters;

/// <summary>Highlights a phase label in the turn tracker when it's the current phase. ConverterParameter is the TurnPhase name to match.</summary>
public sealed class TurnPhaseToBrushConverter : IValueConverter
{
    // Matches the app's dark background (#1A202C) — plain Black/Gray read fine on a light
    // background but Black disappears entirely once the background is dark.
    private static readonly Brush CurrentBrush = new SolidColorBrush(Color.FromRgb(0xF7, 0xFA, 0xFC));
    private static readonly Brush OtherBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = value is TurnPhase phase && parameter is string name && phase.ToString() == name;
        return isCurrent ? CurrentBrush : OtherBrush;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
