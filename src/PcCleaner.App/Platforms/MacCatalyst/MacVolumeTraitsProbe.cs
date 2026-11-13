using Foundation;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>
/// Reads a mount's read-only and hidden flags, and its Finder name, from Foundation's volume resource keys.
/// </summary>
/// <remarks>
/// Read-only resource lookups; nothing on the volume is opened. Time Machine local snapshots, mounted
/// installers and disk images are read-only, and Preboot, VM and Recovery are not browsable, so these two
/// flags are what keeps them off the "Volumes to scan" list. Untested on real hardware, like the rest of the
/// Mac Catalyst target (see #4); any failure returns "nothing known" and the path rules in
/// <c>VolumeCatalog</c> still apply.
/// </remarks>
public sealed class MacVolumeTraitsProbe : IVolumeTraitsProbe
{
    public VolumeProbeResult Probe(string rootPath)
    {
        using NSUrl url = NSUrl.FromFilename(rootPath);
        VolumeTraits traits = VolumeTraits.None;

        if (ReadBool(url, NSUrl.VolumeIsReadOnlyKey) == true)
        {
            traits |= VolumeTraits.ReadOnly;
        }

        if (ReadBool(url, NSUrl.VolumeIsBrowsableKey) == false)
        {
            traits |= VolumeTraits.Hidden;
        }

        string? name = url.TryGetResource(NSUrl.VolumeLocalizedNameKey, out var value) && value is NSString text
            ? text.ToString()
            : null;

        return new VolumeProbeResult(traits, name);
    }

    private static bool? ReadBool(NSUrl url, NSString key) =>
        url.TryGetResource(key, out var value) && value is NSNumber number ? number.BoolValue : null;
}
