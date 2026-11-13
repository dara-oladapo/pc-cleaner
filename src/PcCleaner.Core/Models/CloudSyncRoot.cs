namespace PcCleaner.Core.Models;

/// <summary>The cloud storage service behind a sync root. Drives the label and whether a root is selected by default.</summary>
public enum CloudProvider
{
    Other,
    OneDrive,
    GoogleDrive,
    Dropbox,
    ICloudDrive,
    Box,
    SynologyDrive,
}

/// <summary>
/// One folder that a cloud provider keeps in sync with its service: the OneDrive folder, a Dropbox folder,
/// iCloud Drive, and so on. A user can have several roots for one provider (OneDrive personal and work).
/// </summary>
/// <param name="Id">The platform's identifier for the root. Stable across scans.</param>
/// <param name="DisplayName">What the provider calls it, e.g. "OneDrive - Personal".</param>
/// <param name="Provider">Which service this is, when it can be recognised.</param>
/// <param name="Path">The local folder.</param>
/// <param name="CanFreeUp">
/// False when this app has no supported way to release files in this root, for example a third-party File
/// Provider domain on macOS. Such a root is listed with its sizes, but nothing in it is ever released.
/// </param>
/// <param name="ReadOnlyReason">Why <paramref name="CanFreeUp"/> is false, phrased for the user.</param>
public sealed record CloudSyncRoot(
    string Id,
    string DisplayName,
    CloudProvider Provider,
    string Path,
    bool CanFreeUp,
    string? ReadOnlyReason = null);
