using Avalonia;
using Velopack;

namespace PcCleaner.Desktop;

internal static class Program
{
    // Avalonia's own guidance: nothing here may touch Avalonia types before AppBuilder is configured,
    // because the platform (X11/Wayland, Win32, macOS) hasn't been chosen yet.
    [STAThread]
    public static void Main(string[] args)
    {
        // First, before any UI: Velopack runs its install/update hooks here and exits for those launches. A no-op
        // for a normal launch, and for any copy that isn't a Velopack-built AppImage (see Services/UpdateService).
        VelopackApp.Build().Run();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also what the Avalonia previewer calls, so it must stay public, static, and side-effect free.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            // The X11 WM_CLASS the desktop matches against a menu entry's StartupWMClass. Avalonia defaults it to the
            // assembly name (PcCleaner.Desktop); the .desktop entry vpk embeds in the AppImage says PCCleaner (the
            // packId), as does packaging/linux/pccleaner.desktop. Without this the running window shows up as an
            // unknown app beside its own launcher.
            .With(new X11PlatformOptions { WmClass = "PCCleaner" })
            .WithInterFont()
            .LogToTrace();
}
