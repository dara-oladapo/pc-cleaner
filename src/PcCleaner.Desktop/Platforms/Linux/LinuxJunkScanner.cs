using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Platform.Linux;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// Sizes per-user caches, logs and the home trash, following the XDG base directory spec (#10).
/// </summary>
/// <remarks>
/// <para>
/// Which paths, and which category each lands in, is <see cref="LinuxJunkPlanner"/> in Core, where the rules
/// are unit-tested against an in-memory tree. This class only feeds it the real environment, file system and
/// mount table, then sizes what it returns, the same way <c>WindowsJunkScanner</c> and <c>MacJunkScanner</c> do.
/// </para>
/// <para>
/// The planner has already refused anything reached through a symlink, anything on <c>/proc</c>, <c>/sys</c>,
/// <c>/dev</c> or another pseudo file system, and any root with another device mounted inside it. Inside each
/// root, <see cref="FileSystemWalker"/> never descends into a symlinked directory or counts a symlinked file.
/// </para>
/// </remarks>
public sealed class LinuxJunkScanner : IJunkScanner
{
    private const string MountInfoPath = "/proc/self/mountinfo";

    public async IAsyncEnumerable<JunkItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        // Planning lists a few directories synchronously; keep that off the caller's (UI) thread too.
        IReadOnlyList<LinuxJunkSource> sources = await Task.Run(CreatePlan, cancellationToken);

        int filesSoFar = 0;
        foreach (LinuxJunkSource source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long totalBytes;
            string description;
            if (source.IsDirectory)
            {
                // The walker restarts its count for every root; offset it so the screen's figure keeps climbing.
                (totalBytes, int fileCount) = await FileSystemWalker.ComputeStatsAsync(
                    source.Path, cancellationToken, filesScanned is null ? null : new OffsetProgress(filesScanned, filesSoFar));
                filesSoFar += fileCount;
                description = $"{source.Description} ({fileCount:N0} items)";
            }
            else
            {
                totalBytes = SizeOfRegularFile(source.Path);
                filesScanned?.Report(++filesSoFar);
                description = source.Description;
            }

            if (totalBytes > 0)
            {
                yield return new JunkItem(source.Path, totalBytes, source.Category, description, source.IsDirectory);
            }
        }
    }

    private static IReadOnlyList<LinuxJunkSource> CreatePlan()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!LinuxPath.IsAbsolute(home))
        {
            return [];
        }

        // Fail closed: without the mount table there is no way to tell a cache directory from another disk
        // mounted inside it, and the cleaner would walk straight across. /proc is always there on a desktop.
        LinuxMountTable mounts;
        try
        {
            mounts = LinuxMountTable.ParseMountInfo(File.ReadAllText(MountInfoPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        if (mounts.Mounts.Count == 0)
        {
            return [];
        }

        Func<string, string?> env = Environment.GetEnvironmentVariable;
        XdgBaseDirectories xdg = XdgBaseDirectories.Resolve(env, home);
        return LinuxJunkPlanner.Plan(home, xdg, env, new FileSystemView(), mounts);
    }

    private static long SizeOfRegularFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && !info.Attributes.HasFlag(FileAttributes.ReparsePoint) ? info.Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private sealed class OffsetProgress(IProgress<int> inner, int offset) : IProgress<int>
    {
        public void Report(int value) => inner.Report(offset + value);
    }

    /// <summary>
    /// System.IO's view of the disk. On Unix, .NET sets <see cref="FileAttributes.ReparsePoint"/> from <c>lstat</c>
    /// for a symlink, whatever it points at, which is exactly the "never follow" signal the planner needs.
    /// </summary>
    /// <remarks>
    /// .NET has no portable way to tell a socket, FIFO or device node from a regular file without a <c>stat</c>
    /// P/Invoke, so those come back as <see cref="LinuxEntryKind.File"/>. Sizing one reads only its metadata
    /// (length 0 for all three), so the worst case is a zero-byte row that is never shown.
    /// </remarks>
    private sealed class FileSystemView : ILinuxFileSystemView
    {
        private static readonly EnumerationOptions ListOptions = new()
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false,
        };

        public LinuxEntryKind GetKind(string path)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget is not null)
                {
                    return LinuxEntryKind.Symlink;
                }

                FileAttributes attributes = info.Attributes;
                if ((int)attributes == -1)
                {
                    return LinuxEntryKind.Missing;
                }

                return Classify(attributes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return LinuxEntryKind.Missing;
            }
        }

        public IReadOnlyList<LinuxDirectoryEntry> List(string directory)
        {
            try
            {
                return new DirectoryInfo(directory)
                    .EnumerateFileSystemInfos("*", ListOptions)
                    .Select(info => new LinuxDirectoryEntry(info.Name, Classify(info.Attributes)))
                    .ToList();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }

        private static LinuxEntryKind Classify(FileAttributes attributes)
        {
            if (attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return LinuxEntryKind.Symlink;
            }

            return attributes.HasFlag(FileAttributes.Directory) ? LinuxEntryKind.Directory : LinuxEntryKind.File;
        }
    }
}
