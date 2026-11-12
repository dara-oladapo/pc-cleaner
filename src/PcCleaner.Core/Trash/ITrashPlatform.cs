namespace PcCleaner.Core.Trash;

/// <summary>What <c>lstat</c>/<c>statx</c> says about one directory entry. Only the fields the trash spec needs.</summary>
/// <param name="DeviceMajor">Major part of the device the entry lives on (<c>st_dev</c>).</param>
/// <param name="DeviceMinor">Minor part of the device the entry lives on (<c>st_dev</c>).</param>
/// <param name="MountId">The mount the entry was reached through, matching mountinfo's first field; null when the kernel can't say.</param>
/// <param name="IsDirectory">A real directory (never true for a symlink, even one pointing at a directory).</param>
/// <param name="IsSymbolicLink">The entry itself is a symlink.</param>
/// <param name="IsSticky">The sticky bit (<c>S_ISVTX</c>) is set.</param>
/// <param name="OwnerUid">Owning user id.</param>
/// <param name="Size">Size in bytes of the entry itself (for a symlink, the link, not its target).</param>
public sealed record FileSystemEntryStatus(
    uint DeviceMajor,
    uint DeviceMinor,
    int? MountId,
    bool IsDirectory,
    bool IsSymbolicLink,
    bool IsSticky,
    uint OwnerUid,
    long Size)
{
    /// <summary>True when both entries are on the same device, i.e. a rename between them can succeed.</summary>
    public bool IsSameDevice(FileSystemEntryStatus other) =>
        DeviceMajor == other.DeviceMajor && DeviceMinor == other.DeviceMinor;
}

/// <summary>Result of a rename attempt.</summary>
public enum RenameOutcome
{
    Succeeded,

    /// <summary>Something already exists at the destination; nothing was moved.</summary>
    DestinationExists,

    /// <summary>Source and destination are on different devices (EXDEV); nothing was moved.</summary>
    CrossDevice,

    /// <summary>Any other failure; nothing was moved. The message says why.</summary>
    Failed,
}

/// <summary>
/// The handful of POSIX operations the freedesktop trash needs that .NET has no managed API for, or whose managed
/// API is unsafe here. The Linux implementation is P/Invoke into libc; tests supply a fake.
/// </summary>
/// <remarks>
/// The interface exists so the whole algorithm in <see cref="FreedesktopFileTrasher"/> — home vs per-mount trash,
/// name reservation, info-first ordering, clean-up on failure — is unit-tested on every OS, leaving only thin
/// syscall wrappers untested outside Linux.
/// </remarks>
public interface ITrashPlatform
{
    /// <summary>The real user id of this process (<c>getuid</c>).</summary>
    uint GetUserId();

    /// <summary><c>lstat</c> (or <c>stat</c> when <paramref name="followSymlinks"/>); null if nothing is there.</summary>
    /// <exception cref="IOException">The entry exists but couldn't be examined (e.g. permission denied).</exception>
    FileSystemEntryStatus? GetStatus(string path, bool followSymlinks);

    /// <summary>The text of <c>/proc/self/mountinfo</c>.</summary>
    string ReadMountInfo();

    /// <summary>Creates a single directory with mode 0700. Does nothing if it already exists.</summary>
    /// <exception cref="IOException">It couldn't be created.</exception>
    /// <exception cref="UnauthorizedAccessException">Permission denied.</exception>
    void CreatePrivateDirectory(string path);

    /// <summary>
    /// Creates <paramref name="path"/> exclusively (<c>O_CREAT | O_EXCL</c>, mode 0600) and writes
    /// <paramref name="contents"/> as UTF-8. Returns false, without touching anything, if the name is taken —
    /// that atomic check is what reserves a trash name.
    /// </summary>
    /// <exception cref="IOException">Any other failure (a partially written file is removed first).</exception>
    /// <exception cref="UnauthorizedAccessException">Permission denied.</exception>
    bool TryCreateNewFile(string path, string contents);

    /// <summary>
    /// Renames without replacing an existing destination and without ever falling back to copy-and-delete.
    /// <c>File.Move</c> is not usable here precisely because it silently copies across devices.
    /// </summary>
    RenameOutcome Rename(string source, string destination, out string? error);

    /// <summary>Deletes a file if present; never throws.</summary>
    void TryDeleteFile(string path);
}
