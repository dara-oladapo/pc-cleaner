using System.ComponentModel;
using System.Diagnostics;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// Runs one admin-only operation by relaunching this same exe elevated (ShellExecute "runas", i.e. a UAC prompt) in
/// helper mode — see <see cref="WindowsElevatedHelper"/>. The helper does the one job and exits without showing UI;
/// the app itself never runs elevated.
/// </summary>
public sealed class WindowsElevationService : IElevationService
{
    /// <summary>Win32 ERROR_CANCELLED: the user answered "No" on the UAC prompt.</summary>
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// The system-owned junk roots the helper may clean. Fixed, machine-wide paths only: per-user folders resolve
    /// differently in the elevated process when an admin approves UAC for a standard user, and a failure there is a
    /// locked file that elevation would not fix anyway.
    /// </summary>
    internal static IReadOnlyList<string> SystemJunkRoots
    {
        get
        {
            string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return
            [
                Path.Combine(windowsDir, "Temp"),
                Path.Combine(windowsDir, "Prefetch"),
                Path.Combine(windowsDir, "SoftwareDistribution", "Download"),
            ];
        }
    }

    public bool CanElevate(StartupItem item) => TryDescribe(item, enabled: true, out _);

    public Task<ElevationResult> SetStartupItemEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default) =>
        TryDescribe(item, enabled, out ElevatedOperation? operation)
            ? RunHelperAsync(operation!, cancellationToken)
            : Task.FromResult(ElevationResult.Rejected);

    public IReadOnlyList<string> GetElevatableJunkRoots(IEnumerable<string> failedPaths) =>
        ElevationPolicy.FindContainingRoots(failedPaths, SystemJunkRoots);

    public Task<ElevationResult> CleanJunkRootsAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        if (roots.Count == 0 || !roots.All(r => ElevationPolicy.IsAllowedRoot(r, SystemJunkRoots)))
        {
            return Task.FromResult(ElevationResult.Rejected);
        }

        return RunHelperAsync(new ElevatedOperation(ElevatedOperationKind.CleanJunkRoots, Enabled: false, roots), cancellationToken);
    }

    /// <summary>Maps a startup item to the helper operation that toggles it, if it is one the helper supports.</summary>
    private static bool TryDescribe(StartupItem item, bool enabled, out ElevatedOperation? operation)
    {
        operation = null;
        if (item.Scope != StartupItemScope.AllUsers)
        {
            return false;
        }

        switch (item.Source)
        {
            case StartupItemSource.WindowsRegistryRun when ElevationPolicy.IsValidRegistryValueName(item.Name):
                operation = new ElevatedOperation(ElevatedOperationKind.SetMachineRunEntry, enabled, [item.Name]);
                return true;

            case StartupItemSource.WindowsStartupFolder:
                string activePath = WindowsStartupManager.GetActiveShortcutPath(item.CommandOrPath);
                string commonStartup = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup);
                if (ElevationPolicy.IsFileDirectlyIn(activePath, ".lnk", [commonStartup], out _))
                {
                    operation = new ElevatedOperation(
                        ElevatedOperationKind.SetCommonStartupShortcut, enabled, [Path.GetFileName(activePath)]);
                    return true;
                }

                return false;

            default:
                return false;
        }
    }

    private static async Task<ElevationResult> RunHelperAsync(ElevatedOperation operation, CancellationToken cancellationToken)
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return ElevationResult.Failed;
        }

        string token;
        try
        {
            token = ElevatedOperationCodec.Encode(operation);
        }
        catch (ArgumentException)
        {
            return ElevationResult.Rejected;
        }

        // UseShellExecute + Verb "runas" is what raises the UAC prompt. The token is base64url, so the argument string
        // needs no quoting and nothing in a path or value name can alter how it is split.
        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = $"{ElevatedOperationCodec.HelperVerb} {token}",
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return ElevationResult.Failed;
            }

            await process.WaitForExitAsync(cancellationToken);
            return new ElevationResult(ElevationExitCodes.ToStatus(process.ExitCode));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return ElevationResult.Cancelled;
        }
        catch (Win32Exception)
        {
            return ElevationResult.Failed;
        }
        catch (InvalidOperationException)
        {
            return ElevationResult.Failed;
        }
    }
}
