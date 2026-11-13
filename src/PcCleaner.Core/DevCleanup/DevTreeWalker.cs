using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.DevCleanup;

/// <summary>
/// Bounded folder walks over the developer roots. Never follows a symlink, junction or other reparse point, and
/// never descends into dependency or build-output folders, which can hold hundreds of thousands of entries.
/// </summary>
public static class DevTreeWalker
{
    /// <summary>Folder names never descended into while searching for repositories and projects.</summary>
    public static readonly IReadOnlySet<string> SkippedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "node_modules", "bin", "obj", "target", "build", ".gradle", ".venv", "__pycache__",
        ".next", ".turbo", ".nuxt", ".vs", ".idea",
    };

    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Main repositories (folders with a <c>.git</c> directory) under the roots, at most <paramref name="maxDepth"/>
    /// levels down. A repository's own subfolders are not searched; its linked worktrees come from git itself.
    /// </summary>
    public static IReadOnlyList<string> FindRepositories(IEnumerable<string> roots, int maxDepth, CancellationToken cancellationToken)
    {
        var repos = new List<string>();
        foreach ((string dir, _) in Walk(roots, maxDepth, cancellationToken, stopAt: IsMainRepository))
        {
            if (IsMainRepository(dir))
            {
                repos.Add(dir);
            }
        }

        return repos;
    }

    /// <summary>Every real folder under the roots (the roots included), at most <paramref name="maxDepth"/> levels down.</summary>
    public static IEnumerable<string> EnumerateDirectories(IEnumerable<string> roots, int maxDepth, CancellationToken cancellationToken) =>
        Walk(roots, maxDepth, cancellationToken, stopAt: null).Select(d => d.Path);

    /// <summary>Exists, is a directory, and is not a symlink/junction/reparse point.</summary>
    public static bool IsRealDirectory(string path)
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

    /// <summary>Total size of the real files under <paramref name="path"/>, not following links.</summary>
    public static long MeasureDirectory(string path, CancellationToken cancellationToken)
    {
        long total = 0;
        var pending = new Stack<string>();
        if (IsRealDirectory(path))
        {
            pending.Push(path);
        }

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            foreach (FileSystemInfo entry in SafeList(dir))
            {
                try
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        pending.Push(entry.FullName);
                    }
                    else if (entry is FileInfo file)
                    {
                        total += file.Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return total;
    }

    /// <summary>
    /// The newest write-or-creation time of any file under <paramref name="path"/>, outside the excluded folder
    /// names and not following links. Returns as soon as one at or after <paramref name="stopAtUtc"/> is seen,
    /// since the caller only needs to know the project is recent. Null when there are no files.
    /// </summary>
    public static DateTime? NewestFileTimestampUtc(string path, IReadOnlySet<string> excludedNames, DateTime stopAtUtc, CancellationToken cancellationToken)
    {
        DateTime? newest = null;
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();
            foreach (FileSystemInfo entry in SafeList(dir))
            {
                try
                {
                    if (entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    {
                        continue;
                    }

                    if (entry is DirectoryInfo)
                    {
                        if (!excludedNames.Contains(entry.Name))
                        {
                            pending.Push(entry.FullName);
                        }
                    }
                    else
                    {
                        DateTime stamp = JunkSafetyRules.AgeTimestampUtc(entry.LastWriteTimeUtc, entry.CreationTimeUtc);
                        if (newest is null || stamp > newest)
                        {
                            newest = stamp;
                        }

                        if (stamp >= stopAtUtc)
                        {
                            return newest;
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return newest;
    }

    private static bool IsMainRepository(string dir)
    {
        try
        {
            return Directory.Exists(Path.Combine(dir, ".git"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<(string Path, int Depth)> Walk(
        IEnumerable<string> roots,
        int maxDepth,
        CancellationToken cancellationToken,
        Func<string, bool>? stopAt)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string rawRoot in roots)
        {
            string root;
            try
            {
                root = JunkRootPlanner.Normalize(rawRoot);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!IsRealDirectory(root))
            {
                continue;
            }

            var pending = new Stack<(string Path, int Depth)>();
            pending.Push((root, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string dir, int depth) = pending.Pop();

                // Overlapping roots (~/Dev and a folder inside it) are walked once.
                if (!seen.Add(dir))
                {
                    continue;
                }

                yield return (dir, depth);

                if (depth >= maxDepth || (stopAt is not null && stopAt(dir)))
                {
                    continue;
                }

                foreach (FileSystemInfo entry in SafeList(dir))
                {
                    if (entry is DirectoryInfo sub
                        && !SkippedNames.Contains(sub.Name)
                        && !HasReparseFlag(sub))
                    {
                        pending.Push((sub.FullName, depth + 1));
                    }
                }
            }
        }
    }

    private static bool HasReparseFlag(FileSystemInfo entry)
    {
        try
        {
            return entry.Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static List<FileSystemInfo> SafeList(string dir)
    {
        try
        {
            return new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
