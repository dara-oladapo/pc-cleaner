using System.Globalization;
using Avalonia.Data.Converters;

namespace PcCleaner.Desktop.Converters;

/// <summary>
/// Upper-cases bound text for the <c>eyebrow</c> style. MAUI does this with <c>TextTransform="Uppercase"</c>;
/// Avalonia has no text transform, and the view models' strings ("Browser cache") are shared with MAUI, so the
/// case change belongs in the view.
/// </summary>
public sealed class UppercaseConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s ? s.ToUpper(culture) : value;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
