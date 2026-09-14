using System.Globalization;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>
/// Displays GameStats.TurnCount 1-based. It's stored 0-based internally — GameSession.StartNewGame
/// resets it to 0 and AdvancePhase's turn-1 fast-forward relies on 0 as a sentinel — but "Turn 0"
/// reads as a bug to a player, so this only shifts how it's shown, not the stored value.
/// </summary>
public sealed class TurnCountDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int turnCount ? turnCount + 1 : value ?? 0;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
