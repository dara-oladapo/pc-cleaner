using Foundation;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>
/// Removes the local copy of an iCloud Drive file with <c>NSFileManager.EvictUbiquitous (evictUbiquitousItemAtURL:error:)</c>. The file
/// stays in iCloud and downloads again when opened. Nothing is deleted.
/// </summary>
/// <remarks>
/// Only reached for iCloud Drive: <c>CloudSpaceService</c> refuses paths in roots that cannot be freed, which
/// includes every third-party File Provider folder (see <see cref="MacCloudDriveScanner"/>).
/// </remarks>
public sealed class MacCloudFileReleaser : ICloudFileReleaser
{
    public CloudReleaseAttempt TryRelease(string path, Func<CloudFileState, CloudIneligibility> recheck)
    {
        // Never act on a link: its target could be anywhere.
        try
        {
            if (new FileInfo(path).LinkTarget is not null)
            {
                return new CloudReleaseAttempt(path, CloudReleaseStatus.Skipped, 0, CloudIneligibility.ForeignReparsePoint);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: ex.Message);
        }

        CloudFileState? state = MacCloudDriveScanner.Describe(path);
        if (state is null)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: "Could not read the file's iCloud state.");
        }

        CloudIneligibility reason = recheck(state);
        if (reason != CloudIneligibility.None)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Skipped, 0, reason);
        }

        using NSUrl url = NSUrl.FromFilename(path);
        bool ok = NSFileManager.DefaultManager.EvictUbiquitous(url, out NSError? error);
        return ok
            ? new CloudReleaseAttempt(path, CloudReleaseStatus.Requested, state.AllocatedBytes)
            : new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: error?.LocalizedDescription ?? "Eviction failed.");
    }

    public long? GetAllocatedBytes(string path)
    {
        using NSUrl url = NSUrl.FromFilename(path);
        NSDictionary? values = url.GetResourceValues([NSUrl.TotalFileAllocatedSizeKey], out NSError? error);
        if (error is not null || values is null)
        {
            return null;
        }

        return (values[NSUrl.TotalFileAllocatedSizeKey] as NSNumber)?.Int64Value ?? 0;
    }
}
