using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace PcCleaner.Desktop.Converters;

/// <summary>
/// The icon for a dashboard tool row, picked by the tool's route. MAUI does this with a DataTrigger per route;
/// a converter does it once here.
/// </summary>
public sealed class RouteToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            "duplicates" => "IconDuplicates",
            "large" => "IconLargeFiles",
            "cloud" => "IconCloud",
            "developer" => "IconDeveloper",
            "startup" => "IconStartup",
            _ => "IconJunk",
        };

        return Application.Current?.TryFindResource(key, out object? resource) == true ? resource as Geometry : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// True for a route this front end has a screen for. Cloud Drives and Developer are MAUI-only for now, so their
/// dashboard rows keep the tick and the parts list but drop the "Open" button rather than show one that does nothing.
/// </summary>
public sealed class RouteHasPageConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is "junk" or "duplicates" or "large" or "startup";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
