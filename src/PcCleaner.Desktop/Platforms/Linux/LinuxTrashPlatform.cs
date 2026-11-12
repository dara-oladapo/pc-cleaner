using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;
using PcCleaner.Core.Trash;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>The <see cref="ITrashPlatform"/> syscalls, as P/Invoke into glibc. No NuGet dependency.</summary>
/// <remarks>
/// <para><b>Why <c>statx</c> rather than <c>stat</c>/<c>lstat</c>.</b> The trash spec turns on device identity
/// (can this rename succeed?), ownership, and the sticky bit, none of which .NET exposes. <c>struct stat</c> has a
/// different layout on x86-64, arm64 and 32-bit ARM, so a single managed declaration of it is wrong somewhere;
/// <c>struct statx</c> is defined by the kernel with one fixed layout on every architecture. It also reports the
/// mount id, which matches the first field of <c>/proc/self/mountinfo</c> exactly and so identifies the item's top
/// directory without guessing from path prefixes (which bind mounts and symlinks defeat). Requires Linux 4.11 and
/// glibc 2.28 (2018); the mount id needs Linux 5.8, and without it Core falls back to a prefix match checked
/// against the device number.</para>
/// <para><b>Why <c>renameat2</c>.</b> <c>File.Move</c> silently falls back to copy-and-delete across devices,
/// which is exactly what must never happen here. <c>renameat2(RENAME_NOREPLACE)</c> fails with EXDEV instead,
/// and also refuses to overwrite an item that appeared at the destination after the name was reserved.</para>
/// </remarks>
[SupportedOSPlatform("linux")]
public sealed class LinuxTrashPlatform : ITrashPlatform
{
    public uint GetUserId() => getuid();

    public FileSystemEntryStatus? GetStatus(string path, bool followSymlinks)
    {
        int flags = AT_NO_AUTOMOUNT | (followSymlinks ? 0 : AT_SYMLINK_NOFOLLOW);
        if (statx(AT_FDCWD, path, flags, STATX_BASIC_STATS | STATX_MNT_ID, out StatxBuffer buffer) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno is ENOENT or ENOTDIR)
            {
                return null;
            }

            throw ToException(errno, path);
        }

        int type = buffer.Mode & S_IFMT;
        return new FileSystemEntryStatus(
            buffer.DevMajor,
            buffer.DevMinor,
            (buffer.Mask & STATX_MNT_ID) != 0 ? checked((int)buffer.MountId) : null,
            IsDirectory: type == S_IFDIR,
            IsSymbolicLink: type == S_IFLNK,
            IsSticky: (buffer.Mode & S_ISVTX) != 0,
            OwnerUid: buffer.Uid,
            Size: (long)buffer.Size);
    }

    public string ReadMountInfo() => File.ReadAllText("/proc/self/mountinfo");

    public void CreatePrivateDirectory(string path)
    {
        if (mkdir(path, 0b111_000_000) != 0) // 0700
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno != EEXIST)
            {
                throw ToException(errno, path);
            }
        }
    }

    public bool TryCreateNewFile(string path, string contents)
    {
        // O_EXCL is the reservation: it fails atomically if the name exists, even when two processes race. With
        // O_CREAT it also fails on a symlink (even a dangling one), so nothing is ever written through a link
        // planted in the info directory.
        int fd = open(path, O_WRONLY | O_CREAT | O_EXCL | O_CLOEXEC, 0b110_000_000); // 0600
        if (fd < 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            if (errno == EEXIST)
            {
                return false;
            }

            throw ToException(errno, path);
        }

        try
        {
            using var stream = new FileStream(new SafeFileHandle(fd, ownsHandle: true), FileAccess.Write);
            byte[] bytes = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);
            stream.Write(bytes);
            stream.Flush();
        }
        catch
        {
            TryDeleteFile(path);
            throw;
        }

        return true;
    }

    public RenameOutcome Rename(string source, string destination, out string? error)
    {
        error = null;
        int result;
        int errno;
        try
        {
            result = renameat2(AT_FDCWD, source, AT_FDCWD, destination, RENAME_NOREPLACE);
            errno = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        }
        catch (EntryPointNotFoundException)
        {
            // A libc without renameat2 (glibc < 2.28, older musl).
            result = -1;
            errno = ENOSYS;
        }

        if (result != 0 && errno is EINVAL or ENOSYS)
        {
            // The file system (or libc) doesn't support RENAME_NOREPLACE. The caller has just checked the
            // destination is free, so a plain rename is the best available; it still never copies.
            if (GetStatus(destination, followSymlinks: false) is not null)
            {
                return RenameOutcome.DestinationExists;
            }

            result = rename(source, destination);
            errno = result == 0 ? 0 : Marshal.GetLastPInvokeError();
        }

        if (result == 0)
        {
            return RenameOutcome.Succeeded;
        }

        switch (errno)
        {
            case EEXIST:
            case ENOTEMPTY:
                return RenameOutcome.DestinationExists;
            case EXDEV:
                return RenameOutcome.CrossDevice;
            default:
                error = Marshal.GetPInvokeErrorMessage(errno);
                return RenameOutcome.Failed;
        }
    }

    public void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static Exception ToException(int errno, string path)
    {
        string message = $"{Marshal.GetPInvokeErrorMessage(errno)}: {path}";
        return errno is EACCES or EPERM ? new UnauthorizedAccessException(message) : new IOException(message);
    }

    // errno values (asm-generic, identical on every Linux architecture .NET supports).
    private const int EPERM = 1;
    private const int ENOENT = 2;
    private const int EACCES = 13;
    private const int EEXIST = 17;
    private const int EXDEV = 18;
    private const int ENOTDIR = 20;
    private const int EINVAL = 22;
    private const int ENOSYS = 38;
    private const int ENOTEMPTY = 39;

    private const int AT_FDCWD = -100;
    private const int AT_SYMLINK_NOFOLLOW = 0x100;
    private const int AT_NO_AUTOMOUNT = 0x800;
    private const uint STATX_BASIC_STATS = 0x7FF;
    private const uint STATX_MNT_ID = 0x1000;
    private const uint RENAME_NOREPLACE = 1;

    // open(2) flags. Only flags whose values are the same on x86-64, arm64, arm, ppc64le, s390x, riscv64 and
    // loongarch64; O_NOFOLLOW and O_DIRECTORY are deliberately avoided because arm/arm64/ppc renumber them.
    private const int O_WRONLY = 0x1;
    private const int O_CREAT = 0x40;
    private const int O_EXCL = 0x80;
    private const int O_CLOEXEC = 0x80000;

    private const int S_IFMT = 0xF000;
    private const int S_IFDIR = 0x4000;
    private const int S_IFLNK = 0xA000;
    private const int S_ISVTX = 0x200;

    /// <summary><c>struct statx</c> from linux/stat.h: 256 bytes, same layout on every architecture.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct StatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(20)] public uint Uid;
        [FieldOffset(28)] public ushort Mode;
        [FieldOffset(40)] public ulong Size;
        [FieldOffset(136)] public uint DevMajor;
        [FieldOffset(140)] public uint DevMinor;
        [FieldOffset(144)] public ulong MountId;
    }

    [DllImport("libc", SetLastError = true)]
    private static extern uint getuid();

    [DllImport("libc", SetLastError = true)]
    private static extern int statx(int dirfd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out StatxBuffer buffer);

    [DllImport("libc", SetLastError = true)]
    private static extern int mkdir([MarshalAs(UnmanagedType.LPUTF8Str)] string path, uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int open([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int renameat2(
        int olddirfd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string oldpath,
        int newdirfd,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newpath,
        uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int rename([MarshalAs(UnmanagedType.LPUTF8Str)] string oldpath, [MarshalAs(UnmanagedType.LPUTF8Str)] string newpath);
}
