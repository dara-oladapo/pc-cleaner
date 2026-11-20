using Avalonia.Threading;
using PcCleaner.Desktop.ViewModels;
using PcCleaner.Presentation.Services;

namespace PcCleaner.Desktop.Services;

/// <summary><see cref="INavigationService"/> over the main window's navigation rail (<see cref="ShellViewModel"/>).</summary>
/// <remarks>Routes match the MAUI Shell's: <c>dashboard</c>, <c>junk</c>, <c>duplicates</c>, <c>large</c>, <c>startup</c>, <c>about</c>.</remarks>
public sealed class ShellNavigationService(ShellViewModel shell) : INavigationService
{
    public Task GoToToolAsync(string route) => Dispatcher.UIThread.InvokeAsync(() => shell.Navigate(route)).GetTask();
}
