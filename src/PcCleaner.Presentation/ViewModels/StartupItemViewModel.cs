using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;

namespace PcCleaner.Presentation.ViewModels;

public sealed partial class StartupItemViewModel(StartupItem model) : ObservableObject
{
    public StartupItem Model { get; } = model;

    public string Name => Model.Name;

    public string CommandOrPath => Model.CommandOrPath;

    public string SourceText => Model.Source switch
    {
        StartupItemSource.WindowsRegistryRun => "Registry (Run)",
        StartupItemSource.WindowsStartupFolder => "Startup Folder",
        StartupItemSource.WindowsTaskScheduler => "Task Scheduler",
        StartupItemSource.MacLaunchAgent => "Launch Agent",
        StartupItemSource.MacLaunchDaemon => "Launch Daemon",
        StartupItemSource.MacLoginItem => "Login Item",
        StartupItemSource.LinuxXdgAutostart => "Autostart (.desktop)",
        StartupItemSource.LinuxSystemdUser => "systemd (user)",
        _ => Model.Source.ToString(),
    };

    public string ScopeText => Model.Scope == StartupItemScope.AllUsers ? "All Users" : "Current User";

    /// <summary>All-users entries need admin/root to toggle — surfaced as an attention badge before the user tries.</summary>
    /// <remarks>
    /// Except XDG autostart: a system-wide entry is switched off for this user by a copy in the user's own autostart
    /// directory, which needs no root, so the badge would be wrong there.
    /// </remarks>
    public bool RequiresElevation =>
        Model.Scope == StartupItemScope.AllUsers && Model.Source != StartupItemSource.LinuxXdgAutostart;

    public bool IsEnabled => Model.IsEnabled;

    public string StateText => Model.IsEnabled ? "Enabled" : "Disabled";

    public string ActionButtonText => Model.IsEnabled ? "Disable" : "Enable";
}
