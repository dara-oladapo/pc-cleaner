using System.Globalization;

namespace PcCleaner.App.Converters;

/// <summary>
/// Converts a 0..1 share into a pixel width against a track width passed as ConverterParameter — backs the
/// inline meter bar on list rows.
/// </summary>
/// <remarks>
/// Pass the track width as <c>ConverterParameter="{StaticResource MeterWidth}"</c> so the fill and the
/// <c>MeterTrack</c> style read the same token. Previously each call site hardcoded its own string ('60',
/// '120'), which silently produced a mis-scaled bar whenever one of the pair was edited and the other wasn't.
/// </remarks>
public sealed class ShareToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double share = value is double d ? d : 0;
        return Math.Clamp(share, 0, 1) * TrackWidth(parameter);
    }

    // StaticResource hands over a double; a literal in XAML arrives as a string. Accept both so the
    // token and a hand-written value behave identically.
    private static double TrackWidth(object? parameter) => parameter switch
    {
        double w => w,
        string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) => parsed,
        _ => 132,
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
