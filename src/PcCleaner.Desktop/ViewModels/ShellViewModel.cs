using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop.ViewModels;

/// <summary>One entry in the navigation rail. Routes match the MAUI Shell's.</summary>
/// <param name="IconKey">The resource key of the rail icon's geometry in <c>Resources/Styles/Icons.axaml</c>.</param>
public sealed record NavItem(string Title, string Route, string IconKey);

/// <summary>
/// The Avalonia counterpart of the MAUI <c>AppShell</c>: the navigation rail, the current screen, the theme
/// switcher in the rail footer, and the shared dialog overlay. Desktop-only, because it's about this front end's
/// layout, not app behaviour.
/// </summary>
/// <remarks>
/// <see cref="CurrentPage"/> holds a view model; <c>MainWindow.axaml</c> picks the view through its
/// DataTemplates. Tool view models come from the container, so navigating away and back returns to the same
/// singleton instance — the same lifetime rule the dashboard depends on.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;

    public IReadOnlyList<NavItem> NavItems { get; } =
    [
        new("Dashboard", "dashboard", "IconDashboard"),
        new("Junk Files", "junk", "IconJunk"),
        new("Duplicates", "duplicates", "IconDuplicates"),
        new("Large Files", "large", "IconLargeFiles"),
        new("Startup", "startup", "IconStartup"),
        new("About", "about", "IconAbout"),
    ];

    public DialogHostViewModel Dialog { get; }

    /// <summary>Backs the System / Light / Dark switcher in the rail footer, as on MAUI's flyout footer.</summary>
    public ThemeViewModel Theme { get; }

    [ObservableProperty]
    public partial NavItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    public ShellViewModel(IServiceProvider services, DialogHostViewModel dialog, ThemeViewModel theme)
    {
        _services = services;
        Dialog = dialog;
        Theme = theme;
    }

    /// <summary>The screen the app opens on, as on the MAUI Shell.</summary>
    /// <remarks>
    /// Navigated to by <c>MainWindow</c>, not by this constructor: the dashboard's view model needs
    /// <c>INavigationService</c>, which needs this instance, so resolving it from here would be a circular
    /// dependency the container rejects.
    /// </remarks>
    public const string StartRoute = "dashboard";

    /// <summary>Selects the rail entry for <paramref name="route"/>; an unknown route leaves the current screen alone.</summary>
    public void Navigate(string route)
    {
        var item = NavItems.FirstOrDefault(n => n.Route == route);
        if (item is not null)
        {
            SelectedItem = item;
        }
    }

    partial void OnSelectedItemChanged(NavItem? value)
    {
        if (value is not null)
        {
            CurrentPage = Resolve(value);
        }
    }

    private object Resolve(NavItem item) => item.Route switch
    {
        "dashboard" => _services.GetRequiredService<DashboardViewModel>(),
        "junk" => _services.GetRequiredService<JunkCleanupViewModel>(),
        "duplicates" => _services.GetRequiredService<DuplicateFinderViewModel>(),
        "large" => _services.GetRequiredService<LargeFilesViewModel>(),
        "startup" => _services.GetRequiredService<StartupManagerViewModel>(),
        "about" => _services.GetRequiredService<AboutViewModel>(),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.Route, "No screen for this route."),
    };
}
