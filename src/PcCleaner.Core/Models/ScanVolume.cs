namespace PcCleaner.Core.Models;

/// <summary>Why a volume is on the list, which also decides whether it is scanned by default.</summary>
public enum ScanVolumeKind
{
    /// <summary>The volume the OS runs from. The only one selected by default.</summary>
    System,

    /// <summary>Another internal, physical, local disk.</summary>
    Fixed,

    /// <summary>A USB stick or card. Listed so it can be chosen, but never selected by default.</summary>
    Removable,

    /// <summary>An external or secondary volume on macOS (anything under /Volumes). Not selected by default.</summary>
    External,

    /// <summary>A file-backed disk (VHD/VHDX, Dev Drive, WSL image). Not selected by default.</summary>
    Virtual,
}

/// <summary>A volume the user can choose to scan: local, ready, and not a system, snapshot or read-only mount.</summary>
/// <param name="RootPath">The mount point, and the identity the setting stores.</param>
/// <param name="DisplayName">"Local Disk (C:)", "Macintosh HD", "Backup".</param>
public sealed record ScanVolume(
    string RootPath,
    string DisplayName,
    ScanVolumeKind Kind,
    long TotalBytes,
    long FreeBytes)
{
    public bool IsSystem => Kind == ScanVolumeKind.System;

    /// <summary>Only the system volume is scanned until the user says otherwise.</summary>
    public bool IsSelectedByDefault => IsSystem;

    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    /// <summary>0..1, and 0 for a zero-capacity volume rather than a division by zero.</summary>
    public double UsedShare => TotalBytes <= 0 ? 0 : Math.Clamp((double)UsedBytes / TotalBytes, 0, 1);
}

/// <summary>Everything mounted right now: the volumes a user may choose, plus every mount point for path matching.</summary>
/// <param name="Volumes">The choosable volumes, system volume first.</param>
/// <param name="MountRoots">
/// Every mount point the OS reported, including the ones filtered out of <paramref name="Volumes"/>. A folder
/// on a network share must be recognised as being on that share, not on whichever listed volume happens to
/// be its longest matching prefix ("/" on macOS).
/// </param>
/// <param name="SystemRoot">The system volume's root, even when it could not be read.</param>
/// <param name="PathComparison">How paths compare on this OS.</param>
public sealed record VolumeSnapshot(
    IReadOnlyList<ScanVolume> Volumes,
    IReadOnlyList<string> MountRoots,
    string SystemRoot,
    StringComparison PathComparison);
