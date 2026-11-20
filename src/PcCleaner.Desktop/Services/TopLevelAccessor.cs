using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace PcCleaner.Desktop.Services;

/// <summary>Hands platform services the <see cref="TopLevel"/> they need (storage provider, clipboard, ...) without a reference to a view.</summary>
public interface ITopLevelAccessor
{
    /// <summary>The main window, or null before it exists, after it has closed, or outside a desktop lifetime.</summary>
    TopLevel? Current { get; }
}

/// <summary>Resolves the main window from the running application's desktop lifetime, on every call.</summary>
/// <remarks>
/// Looked up rather than injected: <see cref="MainWindow"/> is itself built by the container from the shell view
/// model, which (through the tool view models) depends on the services that need this. Injecting the window would
/// be a cycle; asking the lifetime at call time is not, and also stays correct if the main window is ever replaced.
/// </remarks>
public sealed class DesktopLifetimeTopLevelAccessor : ITopLevelAccessor
{
    public TopLevel? Current =>
        Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.MainWindow
            : null;
}
