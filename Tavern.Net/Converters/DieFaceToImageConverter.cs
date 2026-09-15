using System.Globalization;
using System.Windows.Data;

namespace Tavern.Net.Converters;

/// <summary>Maps a die's face value (1-6) to its image path under GameData/Images/Dice.</summary>
public sealed class DieFaceToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int face and >= 1 and <= 6 ? $"/GameData/Images/Dice/die_face_{face}.png" : null;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
