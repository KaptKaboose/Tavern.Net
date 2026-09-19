using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tavern.Net.Game;

namespace Tavern.Net.Converters;

/// <summary>Colors a phase label in the turn tracker: green while it's the current phase of your own
/// turn, orange during the opponent's (matching the Your Turn / Opp Turn badge), plain gray otherwise.
/// Bindings are (CurrentPhase, IsMyTurn); ConverterParameter is the TurnPhase name to match.</summary>
public sealed class TurnPhaseToBrushConverter : IMultiValueConverter
{
    private static readonly Brush MyTurnBrush = new SolidColorBrush(Color.FromRgb(0x48, 0xBB, 0x78));
    private static readonly Brush OpponentTurnBrush = new SolidColorBrush(Color.FromRgb(0xF6, 0xAD, 0x55));
    private static readonly Brush OtherBrush = new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0));

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = values.Length > 1 && values[0] is TurnPhase phase && parameter is string name && phase.ToString() == name;
        if (!isCurrent)
        {
            return OtherBrush;
        }

        return values[1] is true ? MyTurnBrush : OpponentTurnBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
