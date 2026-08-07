using Microsoft.Extensions.Logging;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Scanning;
using PcCleaner.App.Pages;
using PcCleaner.App.ViewModels;
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
    }

    private static void RegisterViewModelsAndPages(IServiceCollection services)
    {
        services.AddTransient<JunkCleanupViewModel>();
        services.AddTransient<JunkCleanupPage>();

        services.AddTransient<DuplicateFinderViewModel>();
        services.AddTransient<DuplicateFinderPage>();

        services.AddTransient<StartupManagerViewModel>();
        services.AddTransient<StartupManagerPage>();
    }
}
