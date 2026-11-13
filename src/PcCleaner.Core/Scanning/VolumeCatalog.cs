using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>Which OS's mount layout to interpret. Passed in, not detected, so every rule is testable anywhere.</summary>
public enum VolumePlatform
{
    Windows,
    MacOS,
    Linux,
}

/// <summary>
/// Turns the raw mount list into the volumes a user may choose to scan, and decides which are on by default.
/// </summary>
/// <remarks>
/// <para>The rules, per platform:</para>
/// <list type="bullet">
/// <item>Never offered anywhere: not ready, zero capacity, network shares, optical media, RAM disks, and
/// anything the OS reports as read-only, a snapshot, hidden, an alias of a folder (<c>subst</c>), or a
/// file-system driver with no disk behind it (cloud drives).</item>
/// <item>Windows: <see cref="DriveType.Fixed"/> and <see cref="DriveType.Removable"/> drive letters.
/// File-backed virtual disks (VHD/VHDX, Dev Drive) are listed but not selected.</item>
/// <item>macOS: <c>/</c> plus direct children of <c>/Volumes</c>. Everything else macOS mounts
/// (<c>/System/Volumes/*</c>, <c>/private/var/vm</c>, CoreSimulator runtimes, Time Machine snapshots) is
/// plumbing, not somewhere a user keeps files.</item>
/// <item>Only the system volume is selected by default.</item>
/// </list>
/// <para>
/// <c>/</c> is always offered on macOS even though it is a sealed, read-only snapshot since Catalina: it is
/// how the user thinks of their disk, and paths under it (<c>/Users/…</c>) are firmlinked to the writable
/// data volume.
/// </para>
/// </remarks>
public sealed class VolumeCatalog : IVolumeCatalog
{
    // File systems that are a network share or an optical disc whatever DriveType says. .NET's Unix DriveType
    // mapping covers most of these, but not all of them on every OS version.
    private static readonly HashSet<string> ExcludedFormats = new(StringComparer.OrdinalIgnoreCase)
    {
        "smbfs", "nfs", "nfs4", "afpfs", "webdav", "cifs", "smb2", "smb3", "ftp", "fuse.sshfs", "9p",
        "cd9660", "udf", "cddafs", "iso9660",
        "devfs", "autofs", "tmpfs", "ramfs", "proc", "sysfs", "lxfs",
    };

    // Traits that keep a volume off the list entirely (the system volume excepted).
    private const VolumeTraits NeverListed =
        VolumeTraits.ReadOnly | VolumeTraits.Snapshot | VolumeTraits.Hidden | VolumeTraits.Alias | VolumeTraits.NoBackingDisk;

    private readonly IVolumeSource _source;
    private readonly VolumePlatform _platform;
    private readonly string _systemRoot;

    public VolumeCatalog(IVolumeSource source)
        : this(source, CurrentPlatform(), CurrentSystemRoot())
    {
    }

    public VolumeCatalog(IVolumeSource source, VolumePlatform platform, string systemRoot)
    {
        _source = source;
        _platform = platform;
        _systemRoot = systemRoot;
    }

    public StringComparison PathComparison => ComparisonFor(_platform);

    public VolumeSnapshot Read()
    {
        IReadOnlyList<MountedVolume> mounts = _source.GetMountedVolumes();
        StringComparison comparison = PathComparison;
        var volumes = new List<ScanVolume>();

        foreach (MountedVolume mount in mounts)
        {
            ScanVolume? volume = Classify(mount, comparison);
            if (volume is not null && !volumes.Any(v => string.Equals(v.RootPath, volume.RootPath, comparison)))
            {
                volumes.Add(volume);
            }
        }

        var ordered = volumes
            .OrderBy(v => v.IsSystem ? 0 : 1)
            .ThenBy(v => v.RootPath, StringComparer.FromComparison(comparison))
            .ToList();

        var mountRoots = mounts.Select(m => m.RootPath)
            .Distinct(StringComparer.FromComparison(comparison))
            .ToList();

        return new VolumeSnapshot(ordered, mountRoots, _systemRoot, comparison);
    }

    private ScanVolume? Classify(MountedVolume mount, StringComparison comparison)
    {
        if (!mount.IsReady || mount.TotalBytes <= 0)
        {
            return null;
        }

        if (mount.DriveType is DriveType.Network or DriveType.CDRom or DriveType.Ram or DriveType.NoRootDirectory
            || ExcludedFormats.Contains(mount.Format))
        {
            return null;
        }

        bool isSystem = string.Equals(TrimEndSeparator(mount.RootPath), TrimEndSeparator(_systemRoot), comparison);

        return _platform switch
        {
            VolumePlatform.Windows => ClassifyWindows(mount, isSystem),
            _ => ClassifyUnix(mount, isSystem),
        };
    }

    private static ScanVolume? ClassifyWindows(MountedVolume mount, bool isSystem)
    {
        if (!isSystem && (mount.Traits & NeverListed) != 0)
        {
            return null;
        }

        ScanVolumeKind? kind = isSystem
            ? ScanVolumeKind.System
            : mount.DriveType switch
            {
                DriveType.Fixed when mount.Traits.HasFlag(VolumeTraits.Virtual) => ScanVolumeKind.Virtual,
                DriveType.Fixed => ScanVolumeKind.Fixed,
                DriveType.Removable => ScanVolumeKind.Removable,
                _ => null,
            };

        if (kind is null)
        {
            return null;
        }

        // "C:\" -> "C:". Windows shows an unlabelled disk as "Local Disk", so this does too.
        string letter = TrimEndSeparator(mount.RootPath);
        string label = string.IsNullOrWhiteSpace(mount.Label)
            ? kind == ScanVolumeKind.Removable ? "Removable Disk" : "Local Disk"
            : mount.Label;

        return new ScanVolume(mount.RootPath, $"{label} ({letter})", kind.Value, mount.TotalBytes, mount.FreeBytes);
    }

    private ScanVolume? ClassifyUnix(MountedVolume mount, bool isSystem)
    {
        if (isSystem)
        {
            string systemName = string.IsNullOrWhiteSpace(mount.Label) ? "System volume" : mount.Label;
            return new ScanVolume(mount.RootPath, systemName, ScanVolumeKind.System, mount.TotalBytes, mount.FreeBytes);
        }

        if ((mount.Traits & NeverListed) != 0)
        {
            return null;
        }

        string root = TrimEndSeparator(mount.RootPath);
        if (!IsUserMount(root))
        {
            return null;
        }

        string name = Path.GetFileName(root);

        // Apple's own mounts under /Volumes (Time Machine local snapshots, Xcode's simulator runtimes) are
        // never somewhere the user keeps files, even when they aren't flagged read-only.
        if (name.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Simulator", StringComparison.OrdinalIgnoreCase)
            || mount.Label.Contains("Simulator", StringComparison.OrdinalIgnoreCase)
            || root.Contains(".timemachine", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        ScanVolumeKind kind = mount.Traits.HasFlag(VolumeTraits.Virtual) ? ScanVolumeKind.Virtual
            : mount.DriveType == DriveType.Removable ? ScanVolumeKind.Removable
            : ScanVolumeKind.External;

        string display = string.IsNullOrWhiteSpace(mount.Label) ? name : mount.Label;
        return new ScanVolume(mount.RootPath, display, kind, mount.TotalBytes, mount.FreeBytes);
    }

    /// <summary>Where a user-visible volume is mounted on this OS. Deeper paths are system plumbing.</summary>
    private bool IsUserMount(string root) => _platform switch
    {
        VolumePlatform.MacOS => IsDirectChildOf(root, "/Volumes"),
        _ => IsDirectChildOf(root, "/mnt")
            || root.StartsWith("/media/", StringComparison.Ordinal)
            || root.StartsWith("/run/media/", StringComparison.Ordinal),
    };

    private static bool IsDirectChildOf(string path, string parent) =>
        path.StartsWith(parent + "/", StringComparison.Ordinal)
        && path.Length > parent.Length + 1
        && path.IndexOf('/', parent.Length + 1) < 0;

    internal static string TrimEndSeparator(string path)
    {
        // Keep "/" itself; turn "C:\" into "C:" and "/Volumes/X/" into "/Volumes/X".
        string trimmed = path.TrimEnd('\\', '/');
        return trimmed.Length == 0 ? path[..Math.Min(1, path.Length)] : trimmed;
    }

    public static StringComparison ComparisonFor(VolumePlatform platform) =>
        // APFS and HFS+ are case-insensitive by default, as is NTFS. Linux file systems are not.
        platform == VolumePlatform.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static VolumePlatform CurrentPlatform() =>
        OperatingSystem.IsWindows() ? VolumePlatform.Windows
        : OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst() ? VolumePlatform.MacOS
        : VolumePlatform.Linux;

    /// <summary>"C:\" (wherever Windows is installed) on Windows, "/" elsewhere.</summary>
    public static string CurrentSystemRoot()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "/";
        }

        string? root = Path.GetPathRoot(Environment.SystemDirectory);
        return string.IsNullOrEmpty(root) ? @"C:\" : root;
    }
}
