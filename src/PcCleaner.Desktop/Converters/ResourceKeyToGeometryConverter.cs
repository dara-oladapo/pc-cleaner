using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PcCleaner.Desktop.Converters;

/// <summary>
/// Looks up an icon geometry (<c>IconDashboard</c>, <c>IconJunk</c>, ...) by resource key, so the rail's item
/// template can draw each destination's icon from <see cref="ViewModels.NavItem.IconKey"/>.
/// </summary>
/// <remarks>
/// The MAUI rail carries the geometry itself on <c>IconFlyoutItem.IconData</c>. Here the rail items are plain
/// records built in <see cref="ViewModels.ShellViewModel"/>, which keeps them free of Avalonia types; the key
/// is resolved in the view instead. Icons are theme-independent, so a one-time lookup is enough.
/// </remarks>
public sealed class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key && Application.Current?.TryFindResource(key, out object? resource) == true
            ? resource as Geometry
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
