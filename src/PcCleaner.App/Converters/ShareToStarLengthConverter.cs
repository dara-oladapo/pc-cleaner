using System.Globalization;

namespace PcCleaner.App.Converters;

/// <summary>
/// Turns a 0..1 share into a proportional <see cref="GridLength"/>, so the capacity bar's three segments
/// divide the available width by their real share of the drive rather than by a pixel figure that would
/// stop being true the moment the window is resized.
/// </summary>
public sealed class ShareToStarLengthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double share = value is double d && double.IsFinite(d) ? Math.Clamp(d, 0, 1) : 0;
        return new GridLength(share, GridUnitType.Star);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
