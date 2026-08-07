using System.Globalization;

namespace PcCleaner.App.Converters;

/// <summary>Converts a 0..1 share into a pixel width against a track width passed as ConverterParameter — backs the inline meter bar on list rows.</summary>
public sealed class ShareToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double share = value is double d ? d : 0;
        double trackWidth = parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double w) ? w : 60;
        return Math.Clamp(share, 0, 1) * trackWidth;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
