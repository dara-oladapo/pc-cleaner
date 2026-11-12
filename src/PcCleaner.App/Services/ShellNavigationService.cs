using PcCleaner.Presentation.Services;

namespace PcCleaner.App.Services;

/// <summary><see cref="INavigationService"/> over MAUI Shell's absolute routes (see <c>AppShell.xaml</c>).</summary>
public sealed class ShellNavigationService : INavigationService
{
    // "//" makes the route absolute, so opening a tool from the dashboard switches the selected flyout item
    // rather than pushing the tool onto the dashboard's navigation stack.
    public Task GoToToolAsync(string route) => Shell.Current.GoToAsync($"//{route}");
}
