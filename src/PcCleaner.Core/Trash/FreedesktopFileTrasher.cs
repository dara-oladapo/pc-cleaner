using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Trash;

/// <summary>
/// <see cref="IFileTrasher"/> for freedesktop.org desktops (GNOME, KDE, Xfce, ...), implementing the Trash
/// specification 1.0 directly instead of shelling out to <c>gio trash</c>, so it works without GLib installed.
/// All system calls go through <see cref="ITrashPlatform"/>.
/// </summary>
/// <remarks>
/// <para>For each path:</para>
/// <list type="number">
/// <item>Pick the trash. Same device as the home trash → <c>$XDG_DATA_HOME/Trash</c>, with an absolute
/// <c>Path=</c>. Otherwise the per-mount trash at the top of the item's own file system
/// (<see cref="TrashDirectorySelector"/>), with <c>Path=</c> relative to that top directory so the entry still
/// restores if the drive is mounted somewhere else next time.</item>
/// <item>Reserve a name by creating <c>info/NAME.trashinfo</c> exclusively. The info file is written
/// <em>before</em> the move, as the spec requires: a crash in between leaves an orphaned info file, which is
/// harmless, rather than an item in the trash with no record of where it came from, which can't be restored.</item>
/// <item>Rename the item to <c>files/NAME</c>. A rename, never copy-and-delete: across devices that breaks
/// hard links, is slow on exactly the large files this tool targets, and isn't atomic. If no trash exists on the
/// item's own device, the item is reported as failed and left where it is.</item>
/// </list>
/// <para>Every failure is per item, through <see cref="TrashResult.FailedPaths"/>; one bad path never aborts the
/// batch, and a failed item is never deleted.</para>
/// </remarks>
public sealed class FreedesktopFileTrasher : IFileTrasher
{
    private readonly ITrashPlatform _platform;
    private readonly string? _homeTrash;
    private readonly Func<DateTime> _now;

    /// <param name="platform">The system calls.</param>
    /// <param name="homeTrashDirectory">From <see cref="TrashDirectorySelector.HomeTrashDirectory"/>; null if there is no usable home.</param>
    /// <param name="now">Local clock for <c>DeletionDate</c>; defaults to <see cref="DateTime.Now"/>.</param>
    public FreedesktopFileTrasher(ITrashPlatform platform, string? homeTrashDirectory, Func<DateTime>? now = null)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _homeTrash = homeTrashDirectory;
        _now = now ?? (() => DateTime.Now);
    }

    public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        // Off the caller's thread: sizing a large directory before it moves is blocking I/O, and the caller is
        // usually the UI thread.
        return Task.Run(() => MoveAll(paths, cancellationToken), cancellationToken);
    }

    private TrashResult MoveAll(IReadOnlyList<string> paths, CancellationToken cancellationToken)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        var context = new BatchContext(_platform);

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? error;
            long size;
            try
            {
                error = TrashOne(path, context, out size);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error = ex.Message;
                size = 0;
            }

            if (error is null)
            {
                succeeded.Add(path);
                bytesFreed += size;
            }
            else
            {
                failed.Add((path, error));
            }
        }

        return new TrashResult(succeeded, failed, bytesFreed);
    }

    /// <returns>Null on success, otherwise why the item was left in place.</returns>
    private string? TrashOne(string path, BatchContext context, out long size)
    {
        size = 0;
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Empty path.";
        }

        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string name = Path.GetFileName(fullPath);
        if (name.Length == 0)
        {
            return "A file system root can't be moved to the Trash.";
        }

        FileSystemEntryStatus? item = _platform.GetStatus(fullPath, followSymlinks: false);
        if (item is null)
        {
            return "It no longer exists.";
        }

        MountInfoEntry? mount = context.FindMount(fullPath, item);
        if (mount is not null)
        {
            if (mount.MountPoint == fullPath)
            {
                return $"{fullPath} is a mount point; unmount it instead of trashing it.";
            }

            if (mount.IsReadOnly)
            {
                return $"The drive mounted at {mount.MountPoint} is read-only, so nothing on it can be moved to the Trash.";
            }
        }

        if (!TryResolveTrash(fullPath, item, mount, context, out string? trashDirectory, out string? originalPathField, out string? resolveError))
        {
            return resolveError;
        }

        if (MountInfoParser.IsWithin(fullPath, trashDirectory) || IsWithinNative(fullPath, trashDirectory))
        {
            return "It is already in the Trash.";
        }

        if (MountInfoParser.IsWithin(trashDirectory, fullPath) || IsWithinNative(trashDirectory, fullPath))
        {
            return $"It contains the Trash folder ({trashDirectory}), so it can't be moved into it.";
        }

        size = item.IsDirectory ? DirectorySize(fullPath) : item.Size;

        string filesDirectory = Path.Combine(trashDirectory, "files");
        string infoDirectory = Path.Combine(trashDirectory, "info");
        string contents = TrashInfoFile.Format(originalPathField, _now());

        foreach (string candidate in TrashNameGenerator.Candidates(name))
        {
            string infoPath = Path.Combine(infoDirectory, candidate + TrashInfoFile.Extension);
            if (!_platform.TryCreateNewFile(infoPath, contents))
            {
                continue; // Name taken (by a trashed item or an orphaned info file): try the next.
            }

            string destination = Path.Combine(filesDirectory, candidate);
            if (_platform.GetStatus(destination, followSymlinks: false) is not null)
            {
                // An item with no info file. Leave it alone and take a different name.
                _platform.TryDeleteFile(infoPath);
                continue;
            }

            switch (_platform.Rename(fullPath, destination, out string? renameError))
            {
                case RenameOutcome.Succeeded:
                    return null;

                case RenameOutcome.DestinationExists:
                    _platform.TryDeleteFile(infoPath);
                    continue; // Lost a race for this name.

                case RenameOutcome.CrossDevice:
                    _platform.TryDeleteFile(infoPath);
                    return $"The Trash at {trashDirectory} is on a different file system, and PC Cleaner never copies "
                        + "and deletes instead of moving. Nothing was changed.";

                default:
                    _platform.TryDeleteFile(infoPath);
                    return renameError ?? "The move into the Trash failed.";
            }
        }

        return $"No free name for it in {filesDirectory}.";
    }

    private bool TryResolveTrash(
        string fullPath,
        FileSystemEntryStatus item,
        MountInfoEntry? mount,
        BatchContext context,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? trashDirectory,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? originalPathField,
        out string? error)
    {
        trashDirectory = null;
        originalPathField = null;
        error = null;

        // 1. The home trash, when the item is on the same device as it.
        if (_homeTrash is not null)
        {
            FileSystemEntryStatus? homeDevice = StatNearestExisting(_homeTrash);
            if (homeDevice is not null && homeDevice.IsSameDevice(item))
            {
                try
                {
                    CreateDirectoryChain(_homeTrash);
                    string files = Path.Combine(_homeTrash, "files");
                    _platform.CreatePrivateDirectory(files);
                    _platform.CreatePrivateDirectory(Path.Combine(_homeTrash, "info"));

                    // Re-check where files/ really is: a symlinked Trash could point at another device.
                    FileSystemEntryStatus? filesStatus = _platform.GetStatus(files, followSymlinks: true);
                    if (filesStatus is not null && filesStatus.IsSameDevice(item))
                    {
                        trashDirectory = _homeTrash;
                        originalPathField = fullPath;
                        return true;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    error = $"The Trash folder {_homeTrash} couldn't be prepared: {ex.Message}";
                    return false;
                }
            }
        }

        // 2. The per-mount trash at the top of the item's own file system.
        if (mount is null)
        {
            error = "It is on a different drive from your home Trash, and the drive it is on couldn't be identified "
                + "(/proc/self/mountinfo unavailable). Not moved; PC Cleaner never copies across drives.";
            return false;
        }

        string topDirectory = mount.MountPoint;
        FileSystemEntryStatus? top = _platform.GetStatus(topDirectory, followSymlinks: true);
        if (top is null || !top.IsSameDevice(item))
        {
            // E.g. a btrfs subvolume that isn't mounted on its own: its files have their own device number, so
            // they can't be renamed into a trash at the mount's top directory either.
            error = $"It is on a different file system from both your home Trash and the top of its mount ({topDirectory}), "
                + "so there is no Trash it can be moved into. Not moved; PC Cleaner never copies across drives.";
            return false;
        }

        TrashDirectorySelection selection = TrashDirectorySelector.Select(
            topDirectory, context.UserId, p => _platform.GetStatus(p, followSymlinks: false));

        var problems = new List<string>(selection.Rejections);
        foreach (TrashDirectoryCandidate candidate in selection.Candidates)
        {
            try
            {
                if (candidate.MustCreate)
                {
                    _platform.CreatePrivateDirectory(candidate.Path);
                }

                // Re-validate after creating: something could have raced us to the name.
                FileSystemEntryStatus? created = _platform.GetStatus(candidate.Path, followSymlinks: false);
                if (created is null || created.IsSymbolicLink || !created.IsDirectory || created.OwnerUid != context.UserId)
                {
                    problems.Add($"{candidate.Path} is not a directory owned by you");
                    continue;
                }

                string files = Path.Combine(candidate.Path, "files");
                _platform.CreatePrivateDirectory(files);
                _platform.CreatePrivateDirectory(Path.Combine(candidate.Path, "info"));

                FileSystemEntryStatus? filesStatus = _platform.GetStatus(files, followSymlinks: false);
                if (filesStatus is null || filesStatus.IsSymbolicLink || !filesStatus.IsSameDevice(item))
                {
                    problems.Add($"{files} is not on the same file system as the item");
                    continue;
                }

                trashDirectory = candidate.Path;
                originalPathField = Path.GetRelativePath(topDirectory, fullPath);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{candidate.Path} couldn't be created ({ex.Message})");
            }
        }

        error = $"There is no usable Trash on the drive mounted at {topDirectory}: {string.Join("; ", problems)}. "
            + "Not moved; PC Cleaner never copies across drives.";
        return false;
    }

    /// <summary><c>stat</c> of the path or, if it doesn't exist yet, of its nearest existing ancestor (which is where it would be created).</summary>
    private FileSystemEntryStatus? StatNearestExisting(string path)
    {
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            FileSystemEntryStatus? status = _platform.GetStatus(current, followSymlinks: true);
            if (status is not null)
            {
                return status;
            }
        }

        return null;
    }

    /// <summary>Creates any missing directories down to and including <paramref name="path"/>, each with mode 0700.</summary>
    private void CreateDirectoryChain(string path)
    {
        var missing = new Stack<string>();
        for (string? current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            if (_platform.GetStatus(current, followSymlinks: true) is not null)
            {
                break;
            }

            missing.Push(current);
        }

        while (missing.Count > 0)
        {
            _platform.CreatePrivateDirectory(missing.Pop());
        }
    }

    /// <summary>Same as <see cref="MountInfoParser.IsWithin"/> but with the host's separator, so the fake-platform tests behave on Windows.</summary>
    private static bool IsWithinNative(string path, string directory)
    {
        if (Path.DirectorySeparatorChar == '/')
        {
            return false; // Already covered by the '/' comparison.
        }

        string trimmed = Path.TrimEndingDirectorySeparator(directory);
        return string.Equals(path, trimmed, StringComparison.OrdinalIgnoreCase)
            || (path.Length > trimmed.Length
                && path.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)
                && path[trimmed.Length] == Path.DirectorySeparatorChar);
    }

    /// <summary>Total bytes of the files under a directory, not following symlinks. Best effort.</summary>
    private static long DirectorySize(string directory)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint, // Symlinks: count the link, never what it points at.
        };

        long total = 0;
        try
        {
            foreach (FileInfo file in new DirectoryInfo(directory).EnumerateFiles("*", options))
            {
                try
                {
                    total += file.Length;
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return total;
    }

    /// <summary>Per-batch state: the uid and the mount table are read once, not once per file.</summary>
    private sealed class BatchContext(ITrashPlatform platform)
    {
        private IReadOnlyList<MountInfoEntry>? _mounts;
        private bool _mountsLoaded;
        private uint? _uid;

        public uint UserId => _uid ??= platform.GetUserId();

        public MountInfoEntry? FindMount(string fullPath, FileSystemEntryStatus item)
        {
            if (!_mountsLoaded)
            {
                _mountsLoaded = true;
                try
                {
                    _mounts = MountInfoParser.Parse(platform.ReadMountInfo());
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    _mounts = null;
                }
            }

            if (_mounts is null)
            {
                return null;
            }

            if (item.MountId is int mountId && MountInfoParser.FindByMountId(_mounts, mountId) is { } byId)
            {
                return byId;
            }

            // Older kernels (no STATX_MNT_ID): longest mount-point prefix, accepted only if the device agrees.
            MountInfoEntry? byPrefix = MountInfoParser.FindContainingMount(_mounts, fullPath);
            return byPrefix is not null && byPrefix.DeviceMajor == item.DeviceMajor && byPrefix.DeviceMinor == item.DeviceMinor
                ? byPrefix
                : null;
        }
    }
}
