using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Decides whether a downloaded file in a cloud folder may be returned to online-only.
/// </summary>
/// <remarks>
/// Every rule must hold. When the platform cannot tell (for example the sync state could not be read), the
/// answer is no. Releasing the local copy of a file the cloud does not have in full would lose data, so
/// "probably in sync" is treated as "not in sync".
/// The same rules run twice: once in the scan, and again on the file's state read at the moment of release.
/// </remarks>
public static class CloudEligibilityRules
{
    /// <summary>Files used or changed more recently than this stay on the device.</summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromDays(30);

    /// <summary>Returns the first rule the file fails, or <see cref="CloudIneligibility.None"/> when it passes all of them.</summary>
    public static CloudIneligibility Evaluate(CloudFileState file, DateTime nowUtc, TimeSpan minimumAge)
    {
        CloudIneligibility reason = EvaluateIgnoringInUse(file, nowUtc, minimumAge);
        if (reason != CloudIneligibility.None)
        {
            return reason;
        }

        return file.IsInUse ? CloudIneligibility.InUse : CloudIneligibility.None;
    }

    /// <summary>
    /// Every rule except "not open in another program". Used to decide which files are worth probing, since
    /// the probe itself briefly opens the file.
    /// </summary>
    public static CloudIneligibility EvaluateIgnoringInUse(CloudFileState file, DateTime nowUtc, TimeSpan minimumAge)
    {
        if (!file.IsPlaceholder)
        {
            return CloudIneligibility.NotPlaceholder;
        }

        if (file.HasForeignReparsePoint)
        {
            return CloudIneligibility.ForeignReparsePoint;
        }

        if (!file.IsHydrated || file.AllocatedBytes <= 0)
        {
            return CloudIneligibility.NotHydrated;
        }

        if (file.IsSystemFile)
        {
            return CloudIneligibility.SystemFile;
        }

        if (file.PinState == CloudPinState.Excluded)
        {
            return CloudIneligibility.Excluded;
        }

        if (file.PinState == CloudPinState.Pinned)
        {
            return CloudIneligibility.Pinned;
        }

        if (file.SyncState != CloudSyncState.InSync)
        {
            return CloudIneligibility.NotInSync;
        }

        if (file.ModifiedBytes != 0)
        {
            return CloudIneligibility.LocallyModified;
        }

        if (file.IsTransferring)
        {
            return CloudIneligibility.Transferring;
        }

        if (!IsOldEnough(file, nowUtc, minimumAge))
        {
            return CloudIneligibility.RecentlyUsed;
        }

        return CloudIneligibility.None;
    }

    /// <summary>
    /// True when both the last access and the last write are at least <paramref name="minimumAge"/> ago. A
    /// timestamp in the future (clock skew, or a provider copying the cloud's time) is never old enough.
    /// </summary>
    public static bool IsOldEnough(CloudFileState file, DateTime nowUtc, TimeSpan minimumAge)
    {
        DateTime newest = file.LastAccessUtc > file.LastWriteUtc ? file.LastAccessUtc : file.LastWriteUtc;
        return newest <= nowUtc - minimumAge;
    }

    /// <summary>A short phrase for the UI.</summary>
    public static string Describe(CloudIneligibility reason) => reason switch
    {
        CloudIneligibility.None => "Can be freed",
        CloudIneligibility.NotPlaceholder => "Not managed by the cloud provider",
        CloudIneligibility.ForeignReparsePoint => "Link or special file",
        CloudIneligibility.NotHydrated => "Already online-only",
        CloudIneligibility.SystemFile => "Provider system file",
        CloudIneligibility.Excluded => "Excluded from sync",
        CloudIneligibility.Pinned => "Always kept on this device",
        CloudIneligibility.NotInSync => "Not synced yet",
        CloudIneligibility.LocallyModified => "Has unsynced changes",
        CloudIneligibility.Transferring => "Uploading or downloading",
        CloudIneligibility.RecentlyUsed => "Used recently",
        CloudIneligibility.InUse => "Open in another program",
        CloudIneligibility.OutsideReleasableRoot => "Not in a folder this app can free up",
        _ => reason.ToString(),
    };
}
