namespace PcCleaner.Core.Models;

public enum StartupItemSource
{
    WindowsRegistryRun,
    WindowsStartupFolder,
    WindowsTaskScheduler,
    MacLaunchAgent,
    MacLaunchDaemon,
    MacLoginItem,
}

public enum StartupItemScope
{
    CurrentUser,
    AllUsers,
}
