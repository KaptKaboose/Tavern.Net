using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Tavern.Net.Game;

namespace Tavern.Net.Converters;

/// <summary>Bolds a phase label in the turn tracker when it's the current phase. ConverterParameter is the TurnPhase name to match.</summary>
public sealed class TurnPhaseToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var isCurrent = value is TurnPhase phase && parameter is string name && phase.ToString() == name;
        return isCurrent ? FontWeights.Bold : FontWeights.Normal;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
