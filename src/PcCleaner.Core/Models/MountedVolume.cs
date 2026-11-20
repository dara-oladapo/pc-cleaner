namespace PcCleaner.Core.Models;

/// <summary>
/// Facts about a mount that <see cref="DriveInfo"/> can't tell us and only the OS can: whether it is backed
/// by a disk image, mounted read-only, a snapshot, or hidden from the user's file browser.
/// </summary>
[Flags]
public enum VolumeTraits
{
    None = 0,

    /// <summary>Mounted read-only. Nothing on it can be moved to the Recycle Bin or Trash, so there is nothing to offer.</summary>
    ReadOnly = 1,

    /// <summary>Backed by a file (VHD/VHDX, a WSL or Dev Drive image, a mounted disk image) rather than a physical disk.</summary>
    Virtual = 2,

    /// <summary>A point-in-time snapshot (APFS or Time Machine). Its contents are a copy of another volume.</summary>
    Snapshot = 4,

    /// <summary>The OS hides this mount from the user (macOS MNT_DONTBROWSE): Preboot, VM, Recovery and the like.</summary>
    Hidden = 8,

    /// <summary>
    /// A second name for a folder on another volume (a Windows <c>subst</c> drive). Scanning it would walk
    /// the same files twice and report each as a duplicate of itself.
    /// </summary>
    Alias = 16,

    /// <summary>
    /// Served by a file-system driver with no disk behind it: cloud drives such as Google Drive for desktop,
    /// RAM disks. Walking a cloud drive downloads every file it touches.
    /// </summary>
    NoBackingDisk = 32,
}

/// <summary>One mount as the OS reports it, before any filtering. Produced by an <c>IVolumeSource</c>.</summary>
/// <param name="RootPath">The mount point — "C:\" on Windows, "/" or "/Volumes/Backup" on macOS.</param>
/// <param name="Label">The volume's own name, or empty when it has none or the OS won't say.</param>
/// <param name="Format">The file system name ("NTFS", "apfs", "smbfs"…), or empty when unknown.</param>
/// <param name="TotalBytes">Zero when the volume is not ready.</param>
/// <param name="FreeBytes">Space available to the current user (quota-aware), zero when not ready.</param>
public sealed record MountedVolume(
    string RootPath,
    string Label,
    DriveType DriveType,
    string Format,
    bool IsReady,
    long TotalBytes,
    long FreeBytes,
    VolumeTraits Traits = VolumeTraits.None);
