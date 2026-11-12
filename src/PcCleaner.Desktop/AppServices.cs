using Microsoft.Extensions.DependencyInjection;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Scanning;
using PcCleaner.Desktop.Platforms.Linux;
using PcCleaner.Desktop.Services;
using PcCleaner.Desktop.ViewModels;
using PcCleaner.Presentation.Services;
using PcCleaner.Presentation.ViewModels;

namespace PcCleaner.Desktop;

/// <summary>
/// The Avalonia app's container. Deliberately the same shape as <c>MauiProgram.RegisterServices</c> /
/// <c>RegisterViewModelsAndPages</c> in PcCleaner.App, so the two front ends can be compared line by line.
/// </summary>
public static class AppServices
{
    /// <summary>Set to <c>1</c> in a Debug build to fill the junk list with synthetic results. Never touches disk.</summary>
    public const string FakeScanVariable = "PCCLEANER_FAKE_SCAN";

    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        RegisterServices(services);
        RegisterViewModelsAndViews(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    public static void RegisterServices(IServiceCollection services)
    {
        // Cross-platform: pure System.IO logic, identical on every OS. All three are read-only.
        services.AddSingleton<ILargeFileScanner, LargeFileScanner>();
        services.AddSingleton<IDuplicateFileScanner, DuplicateFileScanner>();
        services.AddSingleton<IDriveSpaceReader, DriveSpaceReader>();

        // Core's real cleaner, now that the Linux scanner (#10) decides what the junk list holds. Junk is
        // regenerable cache, so it is deleted outright, exactly as on Windows and macOS. Only junk: duplicates and
        // large files still go through LinuxFileTrasher below, which stays a stub that moves nothing.
        services.AddSingleton<IJunkCleaner, JunkCleaner>();

        // Platform-specific. Linux is the only platform this front end serves today. The junk scanner is real
        // (and read-only); every other one is a stub that touches nothing, and each has its own issue.
        services.AddSingleton(_ => CreateJunkScanner());                          // #10
        services.AddSingleton<IFileTrasher, LinuxFileTrasher>();                  // #11
        services.AddSingleton<IStartupItemManager, LinuxStartupManager>();        // #12
        services.AddSingleton<IFolderPickerService, LinuxFolderPicker>();         // #13
        services.AddSingleton<IThemeService, LinuxThemeService>();                // #13

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUpdateService, UnsupportedUpdateService>();        // #14
        services.AddSingleton<INavigationService, ShellNavigationService>();
    }

    public static void RegisterViewModelsAndViews(IServiceCollection services)
    {
        // The four tool view models are singletons, not transients, and that is load-bearing: the dashboard
        // composes these same instances, so a scan started from "Scan everything" is the scan you see when
        // you open the tool, and ticking a box on a tool moves the dashboard's combined figure. Making them
        // transient would give each screen its own private copy and quietly break both.
        services.AddSingleton<JunkCleanupViewModel>();
        services.AddSingleton<DuplicateFinderViewModel>();
        services.AddSingleton<LargeFilesViewModel>();
        services.AddSingleton<StartupManagerViewModel>();
        services.AddSingleton<DashboardViewModel>();

        services.AddSingleton<ThemeViewModel>();

        // Singleton: one dialog overlay in the main window, driven by IDialogService.
        services.AddSingleton<DialogHostViewModel>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddTransient<AboutViewModel>();
    }

    private static IJunkScanner CreateJunkScanner()
    {
#if DEBUG
        // Proves the streaming list binds without a real scanner, and without reading or writing anything.
        // Compiled out of Release so a shipped build can never show invented results.
        if (Environment.GetEnvironmentVariable(FakeScanVariable) == "1")
        {
            return new Diagnostics.FakeJunkScanner();
        }
#endif
        return new LinuxJunkScanner();
    }
}
