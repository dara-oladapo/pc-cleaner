namespace PcCleaner.Core.Models;

public enum StartupItemSource
{
    WindowsRegistryRun,
    WindowsStartupFolder,
    WindowsTaskScheduler,
    MacLaunchAgent,
    MacLaunchDaemon,
    MacLoginItem,

    /// <summary>A freedesktop autostart <c>.desktop</c> file ($XDG_CONFIG_HOME/autostart or $XDG_CONFIG_DIRS/autostart).</summary>
    LinuxXdgAutostart,

    /// <summary>A systemd user service, toggled with <c>systemctl --user enable/disable</c>.</summary>
    LinuxSystemdUser,
}

public enum StartupItemScope
{
    CurrentUser,
    AllUsers,
}
