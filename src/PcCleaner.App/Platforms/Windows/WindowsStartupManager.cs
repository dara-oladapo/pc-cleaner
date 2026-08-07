using Microsoft.Win32;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Reads HKCU/HKLM Run keys and the Startup folders. Disabling doesn't delete the entry outright — it's moved to a
/// PcCleaner-owned "quarantine" location (registry key / renamed shortcut) so it can be restored later.
/// </summary>
public sealed class WindowsStartupManager : IStartupItemManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunKeyPath = @"Software\PcCleaner\DisabledStartup\Run";
    private const string DisabledFolderSuffix = ".disabled";

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        items.AddRange(ReadRegistryRun(Registry.CurrentUser, StartupItemScope.CurrentUser));
        items.AddRange(ReadRegistryRun(Registry.LocalMachine, StartupItemScope.AllUsers));
        items.AddRange(ReadDisabledRegistryRun());
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupItemScope.CurrentUser));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupItemScope.AllUsers));

        return Task.FromResult<IReadOnlyList<StartupItem>>(items);
    }

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default)
    {
        switch (item.Source)
        {
            case StartupItemSource.WindowsRegistryRun:
                SetRegistryRunEnabled(item, enabled);
                break;
            case StartupItemSource.WindowsStartupFolder:
                SetStartupFolderEnabled(item, enabled);
                break;
            default:
                throw new NotSupportedException($"{item.Source} is not managed by {nameof(WindowsStartupManager)}.");
        }

        return Task.CompletedTask;
    }

    private static IEnumerable<StartupItem> ReadRegistryRun(RegistryKey hive, StartupItemScope scope)
    {
        using RegistryKey? key = hive.OpenSubKey(RunKeyPath, writable: false);
        if (key is null)
        {
            yield break;
        }

        foreach (string name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string value || string.IsNullOrEmpty(value))
            {
                continue;
            }

            yield return new StartupItem(
                Id: $"run:{hive.Name}:{name}",
                Name: name,
                CommandOrPath: value,
                Source: StartupItemSource.WindowsRegistryRun,
                Scope: scope,
                IsEnabled: true);
        }
    }

    private static IEnumerable<StartupItem> ReadDisabledRegistryRun()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(DisabledRunKeyPath, writable: false);
        if (key is null)
        {
            yield break;
        }

        foreach (string name in key.GetValueNames())
        {
            if (key.GetValue(name) is not string value || string.IsNullOrEmpty(value))
            {
                continue;
            }

            yield return new StartupItem(
                Id: $"run:{Registry.CurrentUser.Name}:{name}",
                Name: name,
                CommandOrPath: value,
                Source: StartupItemSource.WindowsRegistryRun,
                Scope: StartupItemScope.CurrentUser,
                IsEnabled: false);
        }
    }

    private static void SetRegistryRunEnabled(StartupItem item, bool enabled)
    {
        RegistryKey hive = item.Scope == StartupItemScope.AllUsers ? Registry.LocalMachine : Registry.CurrentUser;

        using RegistryKey active = hive.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open '{RunKeyPath}' for write.");
        using RegistryKey disabled = Registry.CurrentUser.CreateSubKey(DisabledRunKeyPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open '{DisabledRunKeyPath}' for write.");

        if (enabled)
        {
            active.SetValue(item.Name, item.CommandOrPath);
            disabled.DeleteValue(item.Name, throwOnMissingValue: false);
        }
        else
        {
            disabled.SetValue(item.Name, item.CommandOrPath);
            active.DeleteValue(item.Name, throwOnMissingValue: false);
        }
    }

    private static IEnumerable<StartupItem> ReadStartupFolder(string folder, StartupItemScope scope)
    {
        if (!Directory.Exists(folder))
        {
            yield break;
        }

        foreach (string file in Directory.EnumerateFiles(folder))
        {
            bool isDisabled = file.EndsWith(DisabledFolderSuffix, StringComparison.OrdinalIgnoreCase);
            string activePath = isDisabled ? file[..^DisabledFolderSuffix.Length] : file;

            if (!isDisabled && !string.Equals(Path.GetExtension(file), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new StartupItem(
                Id: $"folder:{activePath}",
                Name: Path.GetFileNameWithoutExtension(activePath),
                CommandOrPath: file,
                Source: StartupItemSource.WindowsStartupFolder,
                Scope: scope,
                IsEnabled: !isDisabled);
        }
    }

    private static void SetStartupFolderEnabled(StartupItem item, bool enabled)
    {
        string currentPath = item.CommandOrPath;
        bool isCurrentlyDisabled = currentPath.EndsWith(DisabledFolderSuffix, StringComparison.OrdinalIgnoreCase);

        if (enabled && isCurrentlyDisabled)
        {
            File.Move(currentPath, currentPath[..^DisabledFolderSuffix.Length], overwrite: false);
        }
        else if (!enabled && !isCurrentlyDisabled)
        {
            File.Move(currentPath, currentPath + DisabledFolderSuffix, overwrite: false);
        }
    }
}
