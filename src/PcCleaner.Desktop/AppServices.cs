using Microsoft.Extensions.DependencyInjection;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Core.Platform.Linux;
using PcCleaner.Core.Scanning;
using PcCleaner.Core.Settings;
using PcCleaner.Core.Startup.Linux;
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
        // regenerable cache, so it is deleted outright, exactly as on Windows and macOS. The home Trash is the one
        // exception to "delete every file": LinuxJunkCleaner empties it by the freedesktop spec (files/ and info/
        // stay, each entry goes with its .trashinfo). Only junk: duplicates and large files still go through
        // LinuxFileTrasher below, which stays a stub that moves nothing.
        // TODO(#10 follow-up): JunkCleaner does not re-check for mount points at clean time; see LinuxJunkCleaner.
        services.AddSingleton<IJunkCleaner>(_ => new LinuxJunkCleaner(new JunkCleaner()));

        // Platform-specific. Linux is the only platform this front end serves today. The junk scanner (#10), startup
        // manager (#12), folder picker and theme service (#13) are real; every other one is a stub that touches
        // nothing, and each has its own issue.
        services.AddSingleton(_ => CreateJunkScanner());                          // #10
        services.AddSingleton<IFileTrasher, LinuxFileTrasher>();                  // #11
        // #12: XDG autostart + systemd user units. The logic lives in Core (Startup/Linux) so it is unit-tested on
        // every CI platform; this just binds it to the real environment and the real systemctl.
        services.AddSingleton<IStartupItemManager>(_ => LinuxStartupItemManager.CreateForCurrentProcess());
        services.AddSingleton<IFolderPickerService, LinuxFolderPicker>();
        services.AddSingleton<IThemeService, LinuxThemeService>();

        // One settings file for every preference, at $XDG_CONFIG_HOME/pc-cleaner/settings.json. Resolved once:
        // the environment doesn't change under a running process.
        services.AddSingleton<PcCleaner.Core.Settings.ISettingsStore>(_ => new JsonSettingsStore(XdgPaths.SettingsFile()));
        // The Presentation layer has its own (string get/set) settings contract; both views share the one file.
        services.AddSingleton<PcCleaner.Presentation.Services.ISettingsStore, PresentationSettingsStore>();
        services.AddSingleton<ITopLevelAccessor, DesktopLifetimeTopLevelAccessor>();

        // Volumes for the "Volumes to scan" setting; DriveInfo enumerates, there is no Linux traits probe yet.
        // Factories because both classes also have test constructors.
        services.AddSingleton<IVolumeSource>(sp => new DriveInfoVolumeSource(sp.GetService<IVolumeTraitsProbe>()));
        services.AddSingleton<IVolumeCatalog>(sp => new VolumeCatalog(sp.GetRequiredService<IVolumeSource>()));
        services.AddSingleton<ScanVolumeSettings>();

        // Cloud Drives and Developer are MAUI-only tools for now. The dashboard composes their view models, so
        // they are registered, but the Linux scanner finds no sync roots and developer mode has no providers.
        services.AddSingleton<ICloudDriveScanner, InertCloudDriveScanner>();
        services.AddSingleton<ICloudFileReleaser, InertCloudFileReleaser>();
        services.AddSingleton<CloudSpaceService>();
        services.AddSingleton<CloudLocationGuard>();
        services.AddSingleton<DeveloperModeSettings>();
        services.AddSingleton(sp =>
        {
            var developerMode = sp.GetRequiredService<DeveloperModeSettings>();
            return new DevCleanupService([], () => developerMode.IsEnabled);
        });

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IUpdateService, UpdateService>();                   // Velopack; AppImage only
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
        services.AddSingleton<CloudDrivesViewModel>();
        services.AddSingleton<DeveloperCleanupViewModel>();
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
