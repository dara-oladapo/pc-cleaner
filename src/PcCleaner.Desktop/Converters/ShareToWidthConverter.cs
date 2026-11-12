using System.Globalization;
using Avalonia.Data.Converters;

namespace PcCleaner.Desktop.Converters;

/// <summary>
/// Converts a 0..1 share into a pixel width against a track width passed as ConverterParameter — backs the inline
/// meter bar on list rows. The Avalonia counterpart of PcCleaner.App's converter of the same name.
/// </summary>
/// <remarks>
/// Pass the track width as <c>ConverterParameter={StaticResource MeterWidth}</c> so the fill and the
/// <c>meterTrack</c> style read the same token and can't drift apart.
/// </remarks>
public sealed class ShareToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double share = value is double d && double.IsFinite(d) ? d : 0;
        return Math.Clamp(share, 0, 1) * TrackWidth(parameter);
    }

    // StaticResource hands over a double; a literal in XAML arrives as a string. Accept both.
    private static double TrackWidth(object? parameter) => parameter switch
    {
        double w => w,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => 132,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
