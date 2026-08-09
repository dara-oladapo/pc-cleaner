namespace PcCleaner.Core.Models;

/// <summary>The capacity of the drive the user's profile lives on, which is the drive cleaning affects.</summary>
/// <param name="Name">Display name of the drive — "C:\" on Windows, "/" on macOS.</param>
public sealed record DriveSpace(string Name, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => TotalBytes - FreeBytes;

    /// <summary>
    /// The share of the drive occupied by everything that is not the given reclaimable figure, 0..1.
    /// Returns 0 for a zero-capacity drive rather than dividing by zero.
    /// </summary>
    public double OccupiedShare(long reclaimableBytes) =>
        TotalBytes <= 0 ? 0 : Math.Clamp((double)(UsedBytes - ClampReclaimable(reclaimableBytes)) / TotalBytes, 0, 1);

    /// <summary>The share of the drive that a scan says can be freed, 0..1.</summary>
    public double ReclaimableShare(long reclaimableBytes) =>
        TotalBytes <= 0 ? 0 : Math.Clamp((double)ClampReclaimable(reclaimableBytes) / TotalBytes, 0, 1);

    /// <summary>The share of the drive that is already free, 0..1.</summary>
    public double FreeShare => TotalBytes <= 0 ? 0 : Math.Clamp((double)FreeBytes / TotalBytes, 0, 1);

    /// <summary>
    /// A scan can only ever reclaim space that is currently in use. Clamping here keeps the capacity bar
    /// honest if a scan total ever exceeds what is actually occupied (stale results, a drive that changed
    /// under us) instead of drawing a segment wider than the drive.
    /// </summary>
    private long ClampReclaimable(long reclaimableBytes) => Math.Clamp(reclaimableBytes, 0, UsedBytes);
}
