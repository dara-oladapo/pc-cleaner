using System.Security;
using Microsoft.Win32;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Reads HKCU/HKLM Run keys and the Startup folders. Disabling doesn't delete the entry outright — it's moved to a
/// PcCleaner-owned "quarantine" location (registry key / renamed shortcut) so it can be restored later.
/// </summary>
/// <remarks>
/// All-users entries are quarantined in HKLM, beside the key they came from, rather than in HKCU: the move then
/// needs nothing from the signed-in user's hive, so it can run in the elevated helper — which, with over-the-shoulder
/// UAC, may be a different (admin) account with a different HKCU. Without admin rights these operations throw
/// <see cref="ElevationRequiredException"/>, which the UI turns into an offer to retry elevated.
/// </remarks>
public sealed class WindowsStartupManager : IStartupItemManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string DisabledRunKeyPath = @"Software\PcCleaner\DisabledStartup\Run";
    private const string DisabledFolderSuffix = ".disabled";
    private const string ShortcutExtension = ".lnk";

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<StartupItem>();

        items.AddRange(ReadRegistryRun(Registry.CurrentUser, RunKeyPath, StartupItemScope.CurrentUser, isEnabled: true));
        items.AddRange(ReadRegistryRun(Registry.LocalMachine, RunKeyPath, StartupItemScope.AllUsers, isEnabled: true));
        items.AddRange(ReadRegistryRun(Registry.CurrentUser, DisabledRunKeyPath, StartupItemScope.CurrentUser, isEnabled: false));
        items.AddRange(ReadRegistryRun(Registry.LocalMachine, DisabledRunKeyPath, StartupItemScope.AllUsers, isEnabled: false));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupItemScope.CurrentUser));
        items.AddRange(ReadStartupFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupItemScope.AllUsers));

        return Task.FromResult<IReadOnlyList<StartupItem>>(items);
    }

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default)
    {
        switch (item.Source)
        {
            case StartupItemSource.WindowsRegistryRun when item.Scope == StartupItemScope.AllUsers:
                RequireAdmin(() => SetMachineRunEntryEnabled(item.Name, enabled));
                break;
            case StartupItemSource.WindowsRegistryRun:
                SetUserRegistryRunEnabled(item, enabled);
                break;
            case StartupItemSource.WindowsStartupFolder when item.Scope == StartupItemScope.AllUsers:
                RequireAdmin(() => SetStartupFileEnabled(item.CommandOrPath, enabled));
                break;
            case StartupItemSource.WindowsStartupFolder:
                SetStartupFileEnabled(item.CommandOrPath, enabled);
                break;
            default:
                throw new NotSupportedException($"{item.Source} is not managed by {nameof(WindowsStartupManager)}.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Moves an all-users Run value to or from PC Cleaner's HKLM quarantine key. Needs admin rights. Shared with the
    /// elevated helper so both paths do exactly the same thing. The value is copied from the registry itself, kind
    /// included, so a caller only ever names an existing entry — it cannot supply a command to run at startup.
    /// </summary>
    internal static void SetMachineRunEntryEnabled(string valueName, bool enabled)
    {
        string from = enabled ? DisabledRunKeyPath : RunKeyPath;
        string to = enabled ? RunKeyPath : DisabledRunKeyPath;
        MoveRegistryValue(Registry.LocalMachine, from, to, valueName);
    }

    /// <summary>
    /// Renames a shortcut in the all-users Startup folder to or from its ".disabled" name. Needs admin rights. The
    /// folder is resolved here, not passed in, so the elevated helper only ever touches that one folder.
    /// </summary>
    internal static void SetCommonStartupShortcutEnabled(string activeFileName, bool enabled)
    {
        string activePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), activeFileName);
        SetStartupFileEnabled(enabled ? activePath + DisabledFolderSuffix : activePath, enabled);
    }

    /// <summary>The active (enabled) file name of a Startup-folder entry, i.e. without any ".disabled" suffix.</summary>
    internal static string GetActiveShortcutPath(string currentPath) =>
        currentPath.EndsWith(DisabledFolderSuffix, StringComparison.OrdinalIgnoreCase)
            ? currentPath[..^DisabledFolderSuffix.Length]
            : currentPath;

    internal static bool IsShortcutFileName(string fileName) =>
        fileName.EndsWith(ShortcutExtension, StringComparison.OrdinalIgnoreCase);

    private static void RequireAdmin(Action action)
    {
        try
        {
            action();
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new ElevationRequiredException("Changing an all-users startup entry needs administrator permission.", ex);
        }
        catch (SecurityException ex)
        {
            throw new ElevationRequiredException("Changing an all-users startup entry needs administrator permission.", ex);
        }
    }

    private static void MoveRegistryValue(RegistryKey hive, string fromPath, string toPath, string valueName)
    {
        using RegistryKey? source = hive.OpenSubKey(fromPath, writable: true);
        object? value = source?.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

        if (source is null || value is null)
        {
            // Never reported as success: the caller asked to move something that isn't there.
            throw new InvalidOperationException("The startup entry no longer exists.");
        }

        RegistryValueKind kind = source.GetValueKind(valueName);

        using (RegistryKey destination = hive.CreateSubKey(toPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open '{toPath}' for write."))
        {
            // Refuse rather than overwrite: a same-named value already at the destination is someone else's entry.
            if (destination.GetValue(valueName) is not null)
            {
                throw new InvalidOperationException("A startup entry with that name already exists at the destination.");
            }

            destination.SetValue(valueName, value, kind);
        }

        source.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static IEnumerable<StartupItem> ReadRegistryRun(RegistryKey hive, string keyPath, StartupItemScope scope, bool isEnabled)
    {
        using RegistryKey? key = hive.OpenSubKey(keyPath, writable: false);
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
                IsEnabled: isEnabled);
        }
    }

    private static void SetUserRegistryRunEnabled(StartupItem item, bool enabled)
    {
        using RegistryKey active = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
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

            if (!isDisabled && !IsShortcutFileName(file))
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

    private static void SetStartupFileEnabled(string currentPath, bool enabled)
    {
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
