using System.Diagnostics;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>What a junk root's walk found: the exact files to offer, and the folders the walk entered.</summary>
public sealed record JunkRootScan(IReadOnlyList<FileEntry> Files, IReadOnlyList<string> Directories, long TotalBytes);

/// <summary>
/// Walks one planned junk root applying <see cref="JunkSafetyRules"/>. Never follows a symlink, junction or other
/// reparse point (including the root itself), never enters an excluded nested root, and records exactly what it
/// counted so the cleaner can delete that set and nothing else.
/// </summary>
public static class JunkRootScanner
{
    private const int MinProgressIntervalMs = 120;

    // AttributesToSkip = 0: the default skips Hidden|System, which would hide junk from the count but is not a
    // safety measure — reparse points are filtered explicitly below instead, so the intent is visible.
    private static readonly EnumerationOptions ListOneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    public static Task<JunkRootScan> ScanAsync(
        PlannedJunkRoot root,
        DateTime nowUtc,
        TimeSpan minimumAge,
        StringComparison pathComparison,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null) =>
        // Enumeration is blocking I/O; keep it off the caller's (usually UI) thread.
        Task.Run(() => Scan(root, nowUtc, minimumAge, pathComparison, cancellationToken, filesVisited), cancellationToken);

    public static JunkRootScan Scan(
        PlannedJunkRoot root,
        DateTime nowUtc,
        TimeSpan minimumAge,
        StringComparison pathComparison,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        var files = new List<FileEntry>();
        var directories = new List<string>();
        long totalBytes = 0;

        if (!IsRealDirectory(root.Path))
        {
            return new JunkRootScan(files, directories, 0);
        }

        var pending = new Stack<string>();
        pending.Push(root.Path);
        int visited = 0;
        Stopwatch? progressClock = filesVisited is null ? null : Stopwatch.StartNew();

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            bool isRoot = string.Equals(dir, root.Path, pathComparison);

            List<FileSystemInfo> entries;
            try
            {
                entries = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", ListOneLevel).ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (FileSystemInfo entry in entries)
            {
                try
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo subDir)
                    {
                        if (JunkSafetyRules.ShouldSkipDirectory(
                                subDir.Name,
                                root.IsTemp,
                                isDirectChildOfRoot: isRoot,
                                subDir.LastWriteTimeUtc,
                                subDir.CreationTimeUtc,
                                nowUtc,
                                minimumAge)
                            || root.ExcludedPaths.Any(ex => string.Equals(ex, subDir.FullName, pathComparison)))
                        {
                            continue;
                        }

                        directories.Add(subDir.FullName);
                        pending.Push(subDir.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        visited++;
                        if (progressClock is not null && progressClock.ElapsedMilliseconds >= MinProgressIntervalMs)
                        {
                            filesVisited!.Report(visited);
                            progressClock.Restart();
                        }

                        if (JunkSafetyRules.ShouldSkipFile(
                                file.Name, root.IsTemp, file.LastWriteTimeUtc, file.CreationTimeUtc, nowUtc, minimumAge))
                        {
                            continue;
                        }

                        files.Add(new FileEntry(file.FullName, file.Length, file.LastWriteTimeUtc));
                        totalBytes += file.Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Vanished or unreadable between listing and inspection: leave it alone.
                }
            }
        }

        filesVisited?.Report(visited);
        return new JunkRootScan(files, directories, totalBytes);
    }

    /// <summary>Exists, is a directory, and is not itself a symlink/junction/reparse point.</summary>
    internal static bool IsRealDirectory(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint) && info.LinkTarget is null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
