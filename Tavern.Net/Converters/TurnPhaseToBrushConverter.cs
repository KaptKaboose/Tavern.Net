using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Tavern.Net.Game;

namespace Tavern.Net.Converters;

/// <summary>Highlights a phase label in the turn tracker when it's the current phase. ConverterParameter is the TurnPhase name to match.</summary>
public sealed class TurnPhaseToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = value is TurnPhase phase && parameter is string name && phase.ToString() == name;
        return isCurrent ? Brushes.Black : Brushes.Gray;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
