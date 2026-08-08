using System.Globalization;

namespace PcCleaner.App.Converters;

/// <summary>
/// Negates a bool. Used for the "show this only while we are NOT scanning" halves of the scan/result
/// swap, so pages don't need a second observable property that only ever mirrors an existing one.
/// </summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool b || !b;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not bool b || !b;
}
