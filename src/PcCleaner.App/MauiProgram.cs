using Microsoft.Extensions.Logging;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Scanning;
using PcCleaner.App.Pages;
using PcCleaner.App.Services;
using PcCleaner.App.ViewModels;
using Velopack;
#if WINDOWS
using PcCleaner.App.Platforms.Windows;
#elif MACCATALYST
using PcCleaner.App.Platforms.MacCatalyst;
#endif

namespace PcCleaner.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // Must run before anything else: intercepts install/update/uninstall lifecycle command-line
        // hooks from the Velopack-generated installer and exits immediately for those, before any UI
        // would otherwise start up. A no-op when launched normally (not via a Velopack-managed install).
        VelopackApp.Build().Run();

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Aliases are named by role, not by vendor, so swapping a face is a one-line change here
                // rather than a find-and-replace across every style. Body was Open Sans — the MAUI project
                // template's default, and the most anonymous UI face available. IBM Plex Sans was drawn for
                // technical documentation and sits naturally beside JetBrains Mono, which carries every
                // figure and path in the app.
                fonts.AddFont("IBMPlexSans-Regular.ttf", "BodyRegular");
                fonts.AddFont("IBMPlexSans-SemiBold.ttf", "BodySemibold");
                fonts.AddFont("SpaceGrotesk-Medium.ttf", "DisplayMedium");
                fonts.AddFont("SpaceGrotesk-Bold.ttf", "DisplayBold");
                fonts.AddFont("JetBrainsMono-Regular.ttf", "MonoRegular");
                fonts.AddFont("JetBrainsMono-Medium.ttf", "MonoMedium");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        RegisterServices(builder.Services);
        RegisterViewModelsAndPages(builder.Services);

        return builder.Build();
    }

    private static void RegisterServices(IServiceCollection services)
    {
        // Cross-platform: pure System.IO logic, identical on every OS.
        services.AddSingleton<ILargeFileScanner, LargeFileScanner>();
        services.AddSingleton<IDuplicateFileScanner, DuplicateFileScanner>();
        services.AddSingleton<IJunkCleaner, JunkCleaner>();
        services.AddSingleton<IDriveSpaceReader, DriveSpaceReader>();

        // Platform-specific: where junk lives, how autostart works, how the trash/recycle bin works,
        // and how the OS asks the user to pick a folder.
#if WINDOWS
        services.AddSingleton<IJunkScanner, WindowsJunkScanner>();
        services.AddSingleton<IStartupItemManager, WindowsStartupManager>();
        services.AddSingleton<IFileTrasher, WindowsFileTrasher>();
        services.AddSingleton<IFolderPickerService, WindowsFolderPicker>();
#elif MACCATALYST
        services.AddSingleton<IJunkScanner, MacJunkScanner>();
        services.AddSingleton<IStartupItemManager, MacStartupManager>();
        services.AddSingleton<IFileTrasher, MacFileTrasher>();
        services.AddSingleton<IFolderPickerService, MacFolderPicker>();
#endif

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<UpdateService>();
    }

    private static void RegisterViewModelsAndPages(IServiceCollection services)
    {
        // The four tool view models are singletons, not transients, and that is load-bearing: the dashboard
        // composes these same instances, so a scan started from "Scan everything" is the scan you see when
        // you open the tool, and ticking a box on a tool moves the dashboard's combined figure. Making them
        // transient again would give each page its own private copy and quietly break both.
        services.AddSingleton<JunkCleanupViewModel>();
        services.AddSingleton<DuplicateFinderViewModel>();
        services.AddSingleton<LargeFilesViewModel>();
        services.AddSingleton<StartupManagerViewModel>();
        services.AddSingleton<DashboardViewModel>();

        // The shell and its theme switcher live for the life of the app, so the selected segment stays
        // correct without having to be re-read every time a page changes.
        services.AddSingleton<ThemeViewModel>();

        // Singleton: every page hosts a DialogHost, and they all share this one state so whichever page
        // is on screen shows the request. IDialogService drives it.
        services.AddSingleton<DialogHostViewModel>();
        services.AddSingleton<AppShell>();

        services.AddTransient<DashboardPage>();
        services.AddTransient<JunkCleanupPage>();
        services.AddTransient<DuplicateFinderPage>();
        services.AddTransient<LargeFilesPage>();
        services.AddTransient<StartupManagerPage>();

        services.AddTransient<AboutViewModel>();
        services.AddTransient<AboutPage>();
    }
}
