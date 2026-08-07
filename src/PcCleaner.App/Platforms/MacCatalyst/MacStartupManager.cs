using System.ComponentModel;
using System.Diagnostics;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>
/// Reads LaunchAgents/LaunchDaemons plists. Disabling unloads the job via launchctl and moves the plist to a
/// PcCleaner-owned quarantine folder (reversible) instead of deleting it outright.
/// </summary>
public sealed class MacStartupManager : IStartupItemManager
{
    private static readonly string DisabledDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PcCleaner", "DisabledLaunchAgents");

    private static string UserLaunchAgentsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        items.AddRange(ReadPlistDirectory(UserLaunchAgentsDir, StartupItemSource.MacLaunchAgent, StartupItemScope.CurrentUser, isEnabled: true));
        items.AddRange(ReadPlistDirectory("/Library/LaunchAgents", StartupItemSource.MacLaunchAgent, StartupItemScope.AllUsers, isEnabled: true));
        items.AddRange(ReadPlistDirectory("/Library/LaunchDaemons", StartupItemSource.MacLaunchDaemon, StartupItemScope.AllUsers, isEnabled: true));
        items.AddRange(ReadPlistDirectory(DisabledDir, StartupItemSource.MacLaunchAgent, StartupItemScope.CurrentUser, isEnabled: false));

        return Task.FromResult<IReadOnlyList<StartupItem>>(items);
    }

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(DisabledDir);
        string fileName = Path.GetFileName(item.CommandOrPath);

        if (enabled)
        {
            string target = Path.Combine(UserLaunchAgentsDir, fileName);
            if (!File.Exists(target) && File.Exists(item.CommandOrPath))
            {
                Directory.CreateDirectory(UserLaunchAgentsDir);
                File.Move(item.CommandOrPath, target);
            }

            RunLaunchctl($"load \"{target}\"");
        }
        else
        {
            RunLaunchctl($"unload \"{item.CommandOrPath}\"");

            string target = Path.Combine(DisabledDir, fileName);
            if (!File.Exists(target) && File.Exists(item.CommandOrPath))
            {
                File.Move(item.CommandOrPath, target);
            }
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<StartupItem> ReadPlistDirectory(
        string directory, StartupItemSource source, StartupItemScope scope, bool isEnabled)
    {
        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(directory, "*.plist"))
        {
            yield return new StartupItem(
                Id: $"{source}:{file}",
                Name: Path.GetFileNameWithoutExtension(file),
                CommandOrPath: file,
                Source: source,
                Scope: scope,
                IsEnabled: isEnabled);
        }
    }

    private static void RunLaunchctl(string arguments)
    {
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo("launchctl", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            process?.WaitForExit(5000);
        }
        catch (Win32Exception)
        {
            // launchctl unavailable; the plist move still reflects the desired state on next login.
        }
        catch (InvalidOperationException)
        {
        }
    }
}
