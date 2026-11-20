using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// <see cref="IVolumeSource"/> over <see cref="DriveInfo.GetDrives"/>, which lists drive letters on Windows
/// and every mount point on macOS and Linux. Read-only: nothing here opens a file on the volume.
/// </summary>
public sealed class DriveInfoVolumeSource : IVolumeSource
{
    private readonly IVolumeTraitsProbe? _probe;

    public DriveInfoVolumeSource()
        : this(null)
    {
    }

    /// <param name="probe">Optional platform probe for the traits <see cref="DriveInfo"/> can't report.</param>
    public DriveInfoVolumeSource(IVolumeTraitsProbe? probe) => _probe = probe;

    public IReadOnlyList<MountedVolume> GetMountedVolumes()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        var volumes = new List<MountedVolume>(drives.Length);
        foreach (DriveInfo drive in drives)
        {
            volumes.Add(Describe(drive));
        }

        return volumes;
    }

    private MountedVolume Describe(DriveInfo drive)
    {
        string root = drive.Name;
        DriveType type = Safe(() => drive.DriveType, DriveType.Unknown);
        bool ready = Safe(() => drive.IsReady, false);

        if (!ready)
        {
            // An empty card reader or a disconnected mapped drive. Touching its size would throw, and on a
            // dead network mapping could stall — so stop at the name.
            return new MountedVolume(root, string.Empty, type, string.Empty, IsReady: false, 0, 0);
        }

        string label = Safe(() => drive.VolumeLabel, string.Empty);
        string format = Safe(() => drive.DriveFormat, string.Empty);
        long total = Safe(() => drive.TotalSize, 0L);
        long free = Safe(() => drive.AvailableFreeSpace, 0L);

        // Network and optical drives are never offered, so don't spend a probe (or a slow network round
        // trip) on them.
        VolumeProbeResult probe = type is DriveType.Network or DriveType.CDRom || _probe is null
            ? VolumeProbeResult.Unknown
            : SafeProbe(root);

        // On Unix DriveInfo.VolumeLabel is just the mount path again, which says nothing a user would
        // recognise; prefer the OS's own name when the probe has one.
        if (!string.IsNullOrWhiteSpace(probe.DisplayName))
        {
            label = probe.DisplayName;
        }
        else if (string.Equals(label, root, StringComparison.Ordinal))
        {
            label = string.Empty;
        }

        return new MountedVolume(root, label, type, format, IsReady: total > 0, total, free, probe.Traits);
    }

    private VolumeProbeResult SafeProbe(string root)
    {
        try
        {
            return _probe!.Probe(root);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
            or ArgumentException or System.ComponentModel.Win32Exception)
        {
            return VolumeProbeResult.Unknown;
        }
    }

    private static T Safe<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or System.Security.SecurityException)
        {
            return fallback;
        }
    }
}
