using System.ComponentModel;
using System.Diagnostics;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Elevation;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>
/// Runs one root-only operation through <c>osascript</c>'s <c>do shell script … with administrator privileges</c>,
/// which shows the standard macOS admin-password dialog. Only that single shell command runs as root; the app does not.
/// </summary>
/// <remarks>
/// There is no elevated managed code on this side to re-check its input, so all validation happens here, before the
/// script is built: every path must be a plain-named file directly inside one of the fixed system folders below, or
/// exactly one of the fixed junk roots. Each argument is then POSIX single-quoted and the whole command embedded as an
/// AppleScript string (see <see cref="ShellQuoting"/>), and the script reaches osascript as a single argv entry with
/// no shell in between.
/// </remarks>
public sealed class MacElevationService : IElevationService
{
    internal const string SystemLaunchAgentsDir = "/Library/LaunchAgents";
    internal const string SystemLaunchDaemonsDir = "/Library/LaunchDaemons";
    internal const string DisabledSystemLaunchAgentsDir = "/Library/Application Support/PcCleaner/DisabledStartup/LaunchAgents";
    internal const string DisabledSystemLaunchDaemonsDir = "/Library/Application Support/PcCleaner/DisabledStartup/LaunchDaemons";

    /// <summary>The user cancelled the password dialog: AppleScript error -128, "User canceled."</summary>
    private const string UserCancelledErrorCode = "-128";

    private static readonly IReadOnlyList<string> SystemJunkRoots = ["/Library/Caches", "/Library/Logs"];

    public bool CanElevate(StartupItem item) => TryBuildStartupCommand(item, enabled: true, out _);

    public Task<ElevationResult> SetStartupItemEnabledAsync(StartupItem item, bool enabled, CancellationToken cancellationToken = default) =>
        TryBuildStartupCommand(item, enabled, out string? command)
            ? RunAsRootAsync(command!, cancellationToken)
            : Task.FromResult(ElevationResult.Rejected);

    public IReadOnlyList<string> GetElevatableJunkRoots(IEnumerable<string> failedPaths) =>
        ElevationPolicy.FindContainingRoots(failedPaths, SystemJunkRoots);

    public Task<ElevationResult> CleanJunkRootsAsync(IReadOnlyList<string> roots, CancellationToken cancellationToken = default)
    {
        if (roots.Count == 0 || !roots.All(r => ElevationPolicy.IsAllowedRoot(r, SystemJunkRoots)))
        {
            return Task.FromResult(ElevationResult.Rejected);
        }

        // Empties each root but keeps the root itself, matching JunkCleaner. find(1) does not follow symlinks by default
        // and -xdev keeps it on the root's own volume.
        string command = string.Join(" ; ", roots.Select(r =>
            ShellQuoting.JoinPosix("/usr/bin/find", r, "-xdev", "-mindepth", "1", "-delete")));

        return RunAsRootAsync(command, cancellationToken);
    }

    /// <summary>
    /// Disabling moves a system plist into PC Cleaner's root-owned quarantine folder; enabling moves it back. Daemons
    /// are also unloaded/loaded with launchctl. System LaunchAgents load per user session, so for those the change takes
    /// effect at next login rather than by running launchctl as root (which would target the wrong domain).
    /// </summary>
    private static bool TryBuildStartupCommand(StartupItem item, bool enabled, out string? command)
    {
        command = null;
        if (item.Scope != StartupItemScope.AllUsers)
        {
            return false;
        }

        bool isDaemon = item.Source == StartupItemSource.MacLaunchDaemon;
        if (!isDaemon && item.Source != StartupItemSource.MacLaunchAgent)
        {
            return false;
        }

        string activeDir = isDaemon ? SystemLaunchDaemonsDir : SystemLaunchAgentsDir;
        string disabledDir = isDaemon ? DisabledSystemLaunchDaemonsDir : DisabledSystemLaunchAgentsDir;
        string sourceDir = enabled ? disabledDir : activeDir;
        string targetDir = enabled ? activeDir : disabledDir;

        if (!ElevationPolicy.IsFileDirectlyIn(item.CommandOrPath, ".plist", [sourceDir], out _))
        {
            return false;
        }

        string source = Path.Combine(sourceDir, Path.GetFileName(item.CommandOrPath));
        string target = Path.Combine(targetDir, Path.GetFileName(item.CommandOrPath));

        string move =
            $"{ShellQuoting.JoinPosix("/bin/mkdir", "-p", targetDir)}"
            + $" && {ShellQuoting.JoinPosix("/bin/test", "!", "-e", target)}"
            + $" && {ShellQuoting.JoinPosix("/bin/mv", source, target)}";

        if (!isDaemon)
        {
            command = move;
        }
        else if (enabled)
        {
            command = $"{move} && {{ {ShellQuoting.JoinPosix("/bin/launchctl", "load", "-w", target)} || true; }}";
        }
        else
        {
            command = $"{{ {ShellQuoting.JoinPosix("/bin/launchctl", "unload", source)} || true; }} && {move}";
        }

        return true;
    }

    private static async Task<ElevationResult> RunAsRootAsync(string shellCommand, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("/usr/bin/osascript")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(ShellQuoting.BuildAdminAppleScript(shellCommand));

        try
        {
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                return ElevationResult.Failed;
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            await stdout;
            string error = await stderr;

            if (process.ExitCode == 0)
            {
                return ElevationResult.Succeeded;
            }

            return error.Contains(UserCancelledErrorCode, StringComparison.Ordinal)
                ? ElevationResult.Cancelled
                : ElevationResult.Failed;
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
