using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Lists every mount the OS knows about, unfiltered. The seam tests replace with a fake.</summary>
public interface IVolumeSource
{
    IReadOnlyList<MountedVolume> GetMountedVolumes();
}

/// <summary>
/// Reads the facts about a mount that <see cref="DriveInfo"/> doesn't expose (virtual, read-only, snapshot,
/// hidden) and, where the OS has one, a friendlier name. Implemented per platform; read-only queries only.
/// </summary>
public interface IVolumeTraitsProbe
{
    VolumeProbeResult Probe(string rootPath);
}

/// <param name="DisplayName">The OS's own name for the volume ("Macintosh HD"), or null to fall back to the label.</param>
public sealed record VolumeProbeResult(VolumeTraits Traits, string? DisplayName = null)
{
    public static VolumeProbeResult Unknown { get; } = new(VolumeTraits.None);
}

/// <summary>The volumes a user may choose to scan, already filtered for this OS.</summary>
public interface IVolumeCatalog
{
    VolumeSnapshot Read();
}
