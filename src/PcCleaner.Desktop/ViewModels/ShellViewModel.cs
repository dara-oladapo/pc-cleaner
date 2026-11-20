using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop.ViewModels;

/// <summary>One entry in the navigation rail. Routes match the MAUI Shell's.</summary>
public sealed record NavItem(string Title, string Route);

/// <summary>Shown for a tool whose screen hasn't been ported to Avalonia yet.</summary>
public sealed record NotPortedPage(string Title)
{
    public string Message => $"The {Title} screen hasn't been ported to the Linux front end yet (see #9).";
}

/// <summary>
/// The Avalonia counterpart of the MAUI <c>AppShell</c>: the navigation rail, the current screen, and the
/// shared dialog overlay. Desktop-only, because it's about this front end's layout, not app behaviour.
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
        new("Dashboard", "dashboard"),
        new("Junk Files", "junk"),
        new("Duplicates", "duplicates"),
        new("Large Files", "large"),
        new("Startup", "startup"),
        new("About", "about"),
    ];

    public DialogHostViewModel Dialog { get; }

    [ObservableProperty]
    public partial NavItem? SelectedItem { get; set; }

    [ObservableProperty]
    public partial object? CurrentPage { get; set; }

    public ShellViewModel(IServiceProvider services, DialogHostViewModel dialog)
    {
        _services = services;
        Dialog = dialog;

        // Junk Files, not Dashboard, until the dashboard is ported: it's the one screen this scaffold wires.
        Navigate("junk");
    }

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
        "junk" => _services.GetRequiredService<JunkCleanupViewModel>(),
        "about" => _services.GetRequiredService<AboutViewModel>(),
        _ => new NotPortedPage(item.Title),
    };
}
