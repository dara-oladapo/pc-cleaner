using CommunityToolkit.Mvvm.ComponentModel;
using PcCleaner.Core.Models;

namespace PcCleaner.App.ViewModels;

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
        _ => Model.Source.ToString(),
    };

    public string ScopeText => Model.Scope == StartupItemScope.AllUsers ? "All Users" : "Current User";

    /// <summary>All-users entries need admin/root to toggle — surfaced as an attention badge before the user tries.</summary>
    public bool RequiresElevation => Model.Scope == StartupItemScope.AllUsers;

    public bool IsEnabled => Model.IsEnabled;

    public string StateText => Model.IsEnabled ? "Enabled" : "Disabled";

    public string ActionButtonText => Model.IsEnabled ? "Disable" : "Enable";
}
