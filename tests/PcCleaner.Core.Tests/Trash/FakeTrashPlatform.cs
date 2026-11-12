using System.Text;
using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

/// <summary>
/// An <see cref="ITrashPlatform"/> over a real temp directory, using only managed APIs so it runs on every OS.
/// "Devices" are simulated: each registered mount point is its own device, and a rename between devices fails
/// with <see cref="RenameOutcome.CrossDevice"/> exactly like the kernel's EXDEV.
/// </summary>
internal sealed class FakeTrashPlatform : ITrashPlatform
{
    private readonly List<FakeMount> _mounts = [];

    public uint UserId { get; set; } = 1000;

    /// <summary>Directories marked as having the sticky bit (managed APIs can't set it on Windows).</summary>
    public HashSet<string> StickyDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Paths whose owner is reported as a different user.</summary>
    public HashSet<string> ForeignOwned { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Directory creation under these paths fails with permission denied.</summary>
    public HashSet<string> UnwritableDirectories { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Called just before a rename; lets a test observe the state the move happens in.</summary>
    public Action<string, string>? BeforeRename { get; set; }

    /// <summary>Forces the next rename to fail this way.</summary>
    public RenameOutcome? ForceRenameOutcome { get; set; }

    public bool MountInfoUnavailable { get; set; }

    public List<string> Log { get; } = [];

    public void AddMount(int mountId, string mountPoint, uint major, uint minor, bool readOnly = false) =>
        _mounts.Add(new FakeMount(mountId, Path.TrimEndingDirectorySeparator(mountPoint), major, minor, readOnly));

    public uint GetUserId() => UserId;

    public FileSystemEntryStatus? GetStatus(string path, bool followSymlinks)
    {
        var info = new FileInfo(path);
        bool isDirectory = Directory.Exists(path);
        if (!isDirectory && !info.Exists && info.LinkTarget is null)
        {
            return null;
        }

        FakeMount mount = MountFor(path);
        return new FileSystemEntryStatus(
            mount.Major,
            mount.Minor,
            mount.Id,
            IsDirectory: isDirectory && info.LinkTarget is null,
            IsSymbolicLink: !followSymlinks && info.LinkTarget is not null,
            IsSticky: StickyDirectories.Contains(Path.TrimEndingDirectorySeparator(path)),
            OwnerUid: ForeignOwned.Contains(path) ? UserId + 1 : UserId,
            Size: isDirectory ? 4096 : info.Length);
    }

    public string ReadMountInfo()
    {
        if (MountInfoUnavailable)
        {
            throw new IOException("No /proc here.");
        }

        var text = new StringBuilder();
        foreach (FakeMount m in _mounts)
        {
            string options = m.ReadOnly ? "ro,relatime" : "rw,relatime";
            text.Append($"{m.Id} 1 {m.Major}:{m.Minor} / {Escape(m.MountPoint)} {options} shared:1 - ext4 /dev/fake{m.Id} rw\n");
        }

        return text.ToString();
    }

    public void CreatePrivateDirectory(string path)
    {
        Log.Add("mkdir " + path);
        if (UnwritableDirectories.Any(u => IsWithin(path, u)))
        {
            throw new UnauthorizedAccessException($"Permission denied: {path}");
        }

        if (!Directory.Exists(Path.GetDirectoryName(path)))
        {
            throw new IOException($"No such file or directory: {path}");
        }

        Directory.CreateDirectory(path);
    }

    public bool TryCreateNewFile(string path, string contents)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            stream.Write(Encoding.UTF8.GetBytes(contents));
        }
        catch (IOException) when (File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        Log.Add("create " + path);
        return true;
    }

    public RenameOutcome Rename(string source, string destination, out string? error)
    {
        error = null;
        BeforeRename?.Invoke(source, destination);
        Log.Add($"rename {source} -> {destination}");

        if (ForceRenameOutcome is { } forced)
        {
            ForceRenameOutcome = null;
            error = forced == RenameOutcome.Failed ? "Forced failure" : null;
            return forced;
        }

        if (MountFor(source).Id != MountFor(Path.GetDirectoryName(destination)!).Id)
        {
            return RenameOutcome.CrossDevice;
        }

        if (File.Exists(destination) || Directory.Exists(destination))
        {
            return RenameOutcome.DestinationExists;
        }

        if (Directory.Exists(source))
        {
            Directory.Move(source, destination);
        }
        else
        {
            File.Move(source, destination);
        }

        return RenameOutcome.Succeeded;
    }

    public void TryDeleteFile(string path)
    {
        Log.Add("delete " + path);
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private FakeMount MountFor(string path) =>
        _mounts.Where(m => IsWithin(path, m.MountPoint)).OrderBy(m => m.MountPoint.Length).LastOrDefault()
            ?? new FakeMount(0, "", 0, 0, ReadOnly: false); // Outside every registered mount: the "host" device.

    private static bool IsWithin(string path, string directory)
    {
        string p = Path.TrimEndingDirectorySeparator(path);
        string d = Path.TrimEndingDirectorySeparator(directory);
        return string.Equals(p, d, StringComparison.OrdinalIgnoreCase)
            || (p.Length > d.Length && p.StartsWith(d, StringComparison.OrdinalIgnoreCase)
                && (p[d.Length] == Path.DirectorySeparatorChar || p[d.Length] == '/'));
    }

    private static string Escape(string mountPoint) =>
        mountPoint.Replace("\\", "\\134").Replace(" ", "\\040").Replace("\t", "\\011").Replace("\n", "\\012");

    private sealed record FakeMount(int Id, string MountPoint, uint Major, uint Minor, bool ReadOnly);
}
