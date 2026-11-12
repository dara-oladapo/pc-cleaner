using Avalonia;

namespace PcCleaner.Desktop;

internal static class Program
{
    // Avalonia's own guidance: nothing here may touch Avalonia types before AppBuilder is configured,
    // because the platform (X11/Wayland, Win32, macOS) hasn't been chosen yet.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Also what the Avalonia previewer calls, so it must stay public, static, and side-effect free.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
