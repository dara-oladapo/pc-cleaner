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
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
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

        // Platform-specific: where junk lives, how autostart works, how the trash/recycle bin works.
#if WINDOWS
        services.AddSingleton<IJunkScanner, WindowsJunkScanner>();
        services.AddSingleton<IStartupItemManager, WindowsStartupManager>();
        services.AddSingleton<IFileTrasher, WindowsFileTrasher>();
#elif MACCATALYST
        services.AddSingleton<IJunkScanner, MacJunkScanner>();
        services.AddSingleton<IStartupItemManager, MacStartupManager>();
        services.AddSingleton<IFileTrasher, MacFileTrasher>();
#endif

        services.AddSingleton<UpdateService>();
    }

    private static void RegisterViewModelsAndPages(IServiceCollection services)
    {
        services.AddTransient<JunkCleanupViewModel>();
        services.AddTransient<JunkCleanupPage>();

        services.AddTransient<DuplicateFinderViewModel>();
        services.AddTransient<DuplicateFinderPage>();

        services.AddTransient<StartupManagerViewModel>();
        services.AddTransient<StartupManagerPage>();

        services.AddTransient<AboutViewModel>();
        services.AddTransient<AboutPage>();
    }
}
