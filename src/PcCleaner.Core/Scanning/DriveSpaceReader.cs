using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Reads drive capacity via <see cref="DriveInfo"/>, which behaves the same on Windows and macOS, so this
/// stays in Core rather than under Platforms.
/// </summary>
public sealed class DriveSpaceReader : IDriveSpaceReader
{
    private readonly Func<string> _profilePathProvider;

    public DriveSpaceReader()
        : this(() => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    {
    }

    /// <summary>Test seam: lets a test point the reader at a path it controls.</summary>
    public DriveSpaceReader(Func<string> profilePathProvider) => _profilePathProvider = profilePathProvider;

    public DriveSpace? Read()
    {
        try
        {
            string profile = _profilePathProvider();
            if (string.IsNullOrWhiteSpace(profile))
            {
                return null;
            }

            // The profile drive is the one that matters: every junk location and every default scan folder
            // lives under it, so it is the drive the reclaimable figure actually applies to.
            var drive = new DriveInfo(Path.GetPathRoot(profile) ?? profile);
            if (!drive.IsReady || drive.TotalSize <= 0)
            {
                return null;
            }

            // AvailableFreeSpace, not TotalFreeSpace: quota-aware, so it matches what the user can really use.
            return new DriveSpace(drive.Name, drive.TotalSize, drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // A drive we can't interrogate is not an error worth interrupting the app for — the dashboard
            // simply omits the capacity bar and still reports the reclaimable total.
            return null;
        }
    }
}
