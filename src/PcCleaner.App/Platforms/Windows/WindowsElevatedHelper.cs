using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.App.Platforms.Windows;

/// <summary>
/// The elevated side of <see cref="WindowsElevationService"/>: when the exe is started with
/// <see cref="ElevatedOperationCodec.HelperVerb"/>, it performs that one operation and exits before any window opens.
/// </summary>
/// <remarks>
/// This process runs as admin, so it trusts nothing it is given. The payload is decoded strictly and every target is
/// re-validated against the same allowlist the caller used — fixed system junk roots, a plain registry value name, a
/// plain shortcut file name — and anything else exits with <see cref="ElevationExitCodes.Rejected"/> having touched
/// nothing. There is no operation that runs a command or writes caller-supplied data.
/// </remarks>
internal static class WindowsElevatedHelper
{
    /// <summary>Returns true (with the exit code to use) when this process was launched as the elevated helper.</summary>
    public static bool TryRun(IReadOnlyList<string> args, out int exitCode)
    {
        exitCode = ElevationExitCodes.Success;
        if (!ElevatedOperationCodec.TryGetHelperPayload(args, out string? token))
        {
            return false;
        }

        exitCode = Execute(token);
        return true;
    }

    private static int Execute(string? token)
    {
        if (!ElevatedOperationCodec.TryDecode(token, out ElevatedOperation? operation) || operation is null)
        {
            return ElevationExitCodes.Rejected;
        }

        try
        {
            return operation.Kind switch
            {
                ElevatedOperationKind.CleanJunkRoots => CleanJunkRoots(operation.Targets),
                ElevatedOperationKind.SetMachineRunEntry => SetMachineRunEntry(operation.Targets, operation.Enabled),
                ElevatedOperationKind.SetCommonStartupShortcut => SetCommonStartupShortcut(operation.Targets, operation.Enabled),
                _ => ElevationExitCodes.Rejected,
            };
        }
        catch (Exception)
        {
            // No UI and no console in this process: the exit code is the whole report.
            return ElevationExitCodes.Failed;
        }
    }

    private static int CleanJunkRoots(IReadOnlyList<string> roots)
    {
        IReadOnlyList<string> allowed = WindowsElevationService.SystemJunkRoots;
        if (!roots.All(r => ElevationPolicy.IsAllowedRoot(r, allowed)))
        {
            return ElevationExitCodes.Rejected;
        }

        var items = roots
            .Select(r => new JunkItem(r, SizeBytes: 0, JunkCategory.SystemCache, r, IsDirectory: true))
            .ToList();

        // Task.Run keeps the clean off the UI thread's synchronisation context, which this process already has by the
        // time CreateMauiApp runs; blocking on it from there would otherwise deadlock.
        TrashResult result = Task.Run(() => new JunkCleaner().CleanAsync(items)).GetAwaiter().GetResult();
        return result.AllSucceeded ? ElevationExitCodes.Success : ElevationExitCodes.Partial;
    }

    private static int SetMachineRunEntry(IReadOnlyList<string> targets, bool enabled)
    {
        if (targets.Count != 1 || !ElevationPolicy.IsValidRegistryValueName(targets[0]))
        {
            return ElevationExitCodes.Rejected;
        }

        WindowsStartupManager.SetMachineRunEntryEnabled(targets[0], enabled);
        return ElevationExitCodes.Success;
    }

    private static int SetCommonStartupShortcut(IReadOnlyList<string> targets, bool enabled)
    {
        if (targets.Count != 1
            || !ElevationPolicy.IsPlainFileName(targets[0])
            || !WindowsStartupManager.IsShortcutFileName(targets[0]))
        {
            return ElevationExitCodes.Rejected;
        }

        WindowsStartupManager.SetCommonStartupShortcutEnabled(targets[0], enabled);
        return ElevationExitCodes.Success;
    }
}
