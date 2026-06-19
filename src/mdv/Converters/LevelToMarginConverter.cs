using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace mdv.Converters;

/// <summary>
/// Converts a 1-based heading level into a left margin so outline entries indent with
/// their depth (H1 flush-left, each deeper level stepped in by <see cref="IndentPerLevel"/>).
/// </summary>
public sealed class LevelToMarginConverter : IValueConverter
{
    public double IndentPerLevel { get; set; } = 14;

    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int i ? i : 1;
        return new Thickness(Math.Max(0, level - 1) * IndentPerLevel, 0, 0, 0);
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
