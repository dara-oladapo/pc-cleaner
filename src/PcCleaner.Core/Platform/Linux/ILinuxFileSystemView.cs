namespace PcCleaner.Core.Platform.Linux;

public enum LinuxEntryKind
{
    Missing,
    Directory,
    File,

    /// <summary>A symbolic link of any kind, whatever it points at. Never followed.</summary>
    Symlink,

    /// <summary>Sockets, FIFOs, device nodes: never junk, never sized.</summary>
    Other,
}

public sealed record LinuxDirectoryEntry(string Name, LinuxEntryKind Kind);

/// <summary>
/// The two read-only questions <see cref="LinuxJunkPlanner"/> asks of the file system. The real one lives in the
/// Linux front end; tests pass an in-memory tree, so the planning rules run without touching a disk.
/// </summary>
public interface ILinuxFileSystemView
{
    /// <summary>What is at <paramref name="path"/>, without following a symlink there (<c>lstat</c>, not <c>stat</c>).</summary>
    LinuxEntryKind GetKind(string path);

    /// <summary>The immediate children of <paramref name="directory"/>, each classified like <see cref="GetKind"/>. Empty when unreadable.</summary>
    IReadOnlyList<LinuxDirectoryEntry> List(string directory);
}
