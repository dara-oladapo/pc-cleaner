using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Permanently deletes junk (no trash round-trip) but leaves top-level cache/temp directories in place, since apps
/// often assume they still exist.
/// <para>
/// It deletes exactly the files the scan reported in <see cref="JunkItem.Files"/> — never a fresh recursive walk
/// of the root — and re-checks each one first: still inside the root, no symlink/junction on the way to it, not a
/// protected file, and unchanged since the scan. Files that are in use are skipped and reported as skipped, not
/// failed, so they never look like a permissions problem.
/// </para>
/// </summary>
public sealed class JunkCleaner : IJunkCleaner
{
    private readonly StringComparison _pathComparison = JunkRootPlanner.DefaultPathComparison;

    public Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default) =>
        Task.Run(() => Clean(items, cancellationToken), cancellationToken);

    private TrashResult Clean(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken)
    {
        var run = new CleanRun();

        foreach (JunkItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsDirectory)
            {
                CleanDirectoryItem(item, run, cancellationToken);
            }
            else
            {
                CleanFileItem(item, run);
            }
        }

        return new TrashResult(run.Succeeded, run.Failed, run.BytesFreed) { SkippedPaths = run.Skipped };
    }

    private void CleanDirectoryItem(JunkItem item, CleanRun run, CancellationToken cancellationToken)
    {
        string root = JunkRootPlanner.Normalize(item.Path);

        if (!Directory.Exists(root) && !File.Exists(root))
        {
            return;
        }

        if (item.Files is null)
        {
            run.Skipped.Add((item.Path, "No scan manifest; refusing to delete a folder that was not scanned."));
            return;
        }

        if (!JunkRootScanner.IsRealDirectory(root))
        {
            run.Skipped.Add((item.Path, "Folder is a link or junction; not followed."));
            return;
        }

        // Directories already confirmed to be real (not links) on the way down from the root.
        var verified = new HashSet<string>(StringComparer.FromComparison(_pathComparison)) { root };

        foreach (FileEntry file in item.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string path = Path.GetFullPath(file.Path);
            if (!JunkRootPlanner.IsStrictlyUnder(path, root, _pathComparison))
            {
                run.Skipped.Add((file.Path, "Outside the scanned folder."));
                continue;
            }

            if (!PathStaysInsideRoot(path, root, verified))
            {
                run.Skipped.Add((file.Path, "Reached through a link or junction; not followed."));
                continue;
            }

            TryDeleteFile(path, file.LastModifiedUtc, run);
        }

        RemoveEmptyScannedDirectories(item.Directories ?? [], root, verified);
    }

    private static void CleanFileItem(JunkItem item, CleanRun run) => TryDeleteFile(item.Path, expectedLastWriteUtc: null, run);

    /// <summary>True when every folder between <paramref name="root"/> and the file is a real directory.</summary>
    private bool PathStaysInsideRoot(string path, string root, HashSet<string> verified)
    {
        var chain = new List<string>();
        string? dir = Path.GetDirectoryName(path);
        while (dir is not null && !verified.Contains(dir))
        {
            if (!JunkRootPlanner.IsStrictlyUnder(dir, root, _pathComparison))
            {
                return false;
            }

            chain.Add(dir);
            dir = Path.GetDirectoryName(dir);
        }

        if (dir is null)
        {
            return false;
        }

        foreach (string link in chain)
        {
            if (!JunkRootScanner.IsRealDirectory(link))
            {
                return false;
            }

            verified.Add(link);
        }

        return true;
    }

    private static void TryDeleteFile(string path, DateTime? expectedLastWriteUtc, CleanRun run)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
            {
                run.Skipped.Add((path, "Is a link; not followed."));
                return;
            }

            if (JunkSafetyRules.IsProtectedFileName(info.Name))
            {
                run.Skipped.Add((path, "Protected file type."));
                return;
            }

            if (expectedLastWriteUtc is { } expected && info.LastWriteTimeUtc != expected)
            {
                run.Skipped.Add((path, "Changed since the scan."));
                return;
            }

            long size = info.Length;
            info.Delete();
            run.Succeeded.Add(path);
            run.BytesFreed += size;
        }
        catch (IOException ex)
        {
            // Sharing violation / locked / in use (or vanished mid-delete). Another program owns it; more rights
            // would not help, so this is a skip, not a failure.
            run.Skipped.Add((path, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            run.Failed.Add((path, ex.Message));
        }
    }

    /// <summary>
    /// Removes only folders the scan entered (so never an excluded, recent, protected or linked one), deepest
    /// first, only when now empty, and never the root itself. Each is re-checked for being a link immediately
    /// before deletion; a non-recursive delete cannot reach anything beneath it anyway.
    /// </summary>
    private void RemoveEmptyScannedDirectories(IReadOnlyList<string> directories, string root, HashSet<string> verified)
    {
        foreach (string dir in directories.Select(Path.GetFullPath).OrderByDescending(d => d.Length))
        {
            if (!JunkRootPlanner.IsStrictlyUnder(dir, root, _pathComparison))
            {
                continue;
            }

            try
            {
                if (!PathStaysInsideRoot(Path.Combine(dir, "_"), root, verified)
                    || Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    continue;
                }

                if (JunkRootScanner.IsRealDirectory(dir))
                {
                    Directory.Delete(dir, recursive: false);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort tidy-up: an undeletable empty folder is not worth surfacing.
            }
        }
    }

    private sealed class CleanRun
    {
        public List<string> Succeeded { get; } = [];

        public List<(string Path, string Error)> Failed { get; } = [];

        public List<(string Path, string Reason)> Skipped { get; } = [];

        public long BytesFreed { get; set; }
    }
}
