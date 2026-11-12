using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Permanently deletes junk (no trash round-trip) but leaves top-level cache/temp directories in place, since apps
/// often assume they still exist.
/// <para>
/// It deletes exactly the files the scan reported in <see cref="JunkItem.Files"/> — never a fresh recursive walk
/// of the root — and re-checks each one immediately before deleting it: its manifest path is canonical and inside
/// the root, every folder from the root down to it is a real directory (no symlink/junction), it is not a
/// protected type, its size, write time and creation time are unchanged since the scan, and (for temp roots) it is
/// still old enough by the clock right now. Files that are in use are skipped and reported as skipped, not failed,
/// so they never look like a permissions problem.
/// </para>
/// </summary>
// TODO: The link checks here are path-based, so a folder swapped for a junction between the check and the delete
// (a TOCTOU race) is narrowed but not closed. The full fix is handle-based deletion: open each directory from the
// root down with FILE_FLAG_OPEN_REPARSE_POINT / O_NOFOLLOW, verify it is not a reparse point on the handle, and
// delete relative to that handle (NtSetInformationFile FileDispositionInfo / unlinkat).
public sealed class JunkCleaner : IJunkCleaner
{
    private readonly StringComparison _pathComparison = JunkRootPlanner.DefaultPathComparison;
    private readonly TimeProvider _timeProvider;

    public JunkCleaner()
        : this(TimeProvider.System)
    {
    }

    public JunkCleaner(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default) =>
        Task.Run(() => Clean(items, cancellationToken), cancellationToken);

    private TrashResult Clean(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken)
    {
        var run = new CleanRun();

        foreach (JunkItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.Notice is not null)
            {
                continue;
            }

            if (item.IsDirectory)
            {
                CleanDirectoryItem(item, run, cancellationToken);
            }
            else
            {
                TryDeleteFile(item.Path, expected: null, minimumAge: null, run);
            }
        }

        return new TrashResult(run.Succeeded, run.Failed, run.BytesFreed) { SkippedPaths = run.Skipped };
    }

    private void CleanDirectoryItem(JunkItem item, CleanRun run, CancellationToken cancellationToken)
    {
        string root = JunkRootPlanner.Normalize(item.Path);

        if (!Directory.Exists(root) && !File.Exists(root) && !JunkRootScanner.IsLink(root))
        {
            return;
        }

        if (item.Files is null)
        {
            run.Skipped.Add((item.Path, "No scan manifest; refusing to delete a folder that was not scanned."));
            return;
        }

        foreach (JunkFileEntry file in item.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsCanonicalPathInside(file.Path, root))
            {
                run.Skipped.Add((file.Path, "Not a plain path inside the scanned folder."));
                continue;
            }

            if (!ChainIsReal(file.Path, root))
            {
                run.Skipped.Add((file.Path, "Reached through a link or junction; not followed."));
                continue;
            }

            TryDeleteFile(file.Path, file, item.MinimumAge, run);
        }

        RemoveEmptyScannedDirectories(item.Directories ?? [], root, item.MinimumAge);
    }

    /// <summary>
    /// Manifest paths are used exactly as the scan produced them. One that is not already canonical (contains
    /// "..", odd separators, trailing dots...) is refused rather than normalised into something else.
    /// </summary>
    private bool IsCanonicalPathInside(string path, string root)
    {
        try
        {
            return string.Equals(Path.GetFullPath(path), path, StringComparison.Ordinal)
                && JunkRootPlanner.IsStrictlyUnder(path, root, _pathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// True when the root and every folder between it and <paramref name="path"/> is a real directory. Checked
    /// fresh for every single delete: no cache, so a folder swapped for a junction mid-clean is still caught.
    /// </summary>
    private bool ChainIsReal(string path, string root)
    {
        string? dir = Path.GetDirectoryName(path);
        while (dir is not null && JunkRootPlanner.IsStrictlyUnder(dir, root, _pathComparison))
        {
            if (!JunkRootScanner.IsRealDirectory(dir))
            {
                return false;
            }

            dir = Path.GetDirectoryName(dir);
        }

        return dir is not null
            && string.Equals(dir, root, _pathComparison)
            && JunkRootScanner.IsRealDirectory(root);
    }

    private void TryDeleteFile(string path, JunkFileEntry? expected, TimeSpan? minimumAge, CleanRun run)
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

            if (expected is not null
                && (info.Length != expected.SizeBytes
                    || info.LastWriteTimeUtc != expected.LastWriteUtc
                    || info.CreationTimeUtc != expected.CreationUtc))
            {
                run.Skipped.Add((path, "Changed since the scan."));
                return;
            }

            if (minimumAge is { } age
                && !JunkSafetyRules.IsOldEnough(info.LastWriteTimeUtc, info.CreationTimeUtc, _timeProvider.GetUtcNow().UtcDateTime, age))
            {
                run.Skipped.Add((path, "Too recent to remove."));
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
    /// first, only when now empty, and never the root itself. Each is re-checked immediately before deletion: the
    /// whole chain from the root is real directories, it has not been recreated since the scan, and for temp roots
    /// it is still old enough. Age here is by creation time only, because deleting a folder's contents moments
    /// ago has just updated its write time. A non-recursive delete cannot reach anything beneath it anyway.
    /// </summary>
    private void RemoveEmptyScannedDirectories(IReadOnlyList<JunkDirectoryEntry> directories, string root, TimeSpan? minimumAge)
    {
        foreach (JunkDirectoryEntry entry in directories.OrderByDescending(d => d.Path.Length))
        {
            string dir = entry.Path;
            if (!IsCanonicalPathInside(dir, root))
            {
                continue;
            }

            try
            {
                if (!ChainIsReal(Path.Combine(dir, "_"), root))
                {
                    continue;
                }

                var info = new DirectoryInfo(dir);
                if (info.CreationTimeUtc != entry.CreationUtc)
                {
                    continue;
                }

                if (minimumAge is { } age
                    && !JunkSafetyRules.IsOldEnough(info.CreationTimeUtc, info.CreationTimeUtc, _timeProvider.GetUtcNow().UtcDateTime, age))
                {
                    continue;
                }

                if (info.EnumerateFileSystemInfos().Any())
                {
                    continue;
                }

                Directory.Delete(dir, recursive: false);
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
