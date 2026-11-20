namespace PcCleaner.Core.Models;

/// <summary>Whether the provider says the local copy matches the cloud copy.</summary>
public enum CloudSyncState
{
    /// <summary>The state could not be read. Treated exactly like <see cref="NotInSync"/>.</summary>
    Unknown,
    InSync,
    NotInSync,
}

/// <summary>The user's intent for a file, as recorded by the platform (Windows CF_PIN_STATE).</summary>
public enum CloudPinState
{
    Unspecified,

    /// <summary>"Always keep on this device". Never released.</summary>
    Pinned,

    /// <summary>Already marked online-only; the provider has not released it yet.</summary>
    Unpinned,

    /// <summary>Excluded from sync. The cloud may not have it at all, so it is never released.</summary>
    Excluded,

    Inherit,
}

/// <summary>
/// What the platform reports about one local file inside a cloud sync root. Platform scanners fill this in;
/// <c>CloudEligibilityRules</c> decides whether the file may be returned to online-only.
/// </summary>
/// <param name="Path">Full local path.</param>
/// <param name="LogicalBytes">The file's length.</param>
/// <param name="AllocatedBytes">Bytes the file actually occupies on disk. This is what releasing it frees.</param>
/// <param name="LastAccessUtc">Last access time, which some volumes do not update.</param>
/// <param name="LastWriteUtc">Last write time.</param>
/// <param name="IsPlaceholder">True when the file is a cloud placeholder (Windows) or a ubiquitous item (macOS).</param>
/// <param name="IsHydrated">True when the file's content is on this device.</param>
/// <param name="HasForeignReparsePoint">True when the file is a link or carries a reparse point other than the cloud one.</param>
/// <param name="SyncState">Whether the provider says the local copy is the same as the cloud copy.</param>
/// <param name="ModifiedBytes">Bytes changed locally that the cloud does not have yet (Windows only; 0 elsewhere).</param>
/// <param name="PinState">The user's keep-on-device intent.</param>
/// <param name="IsInUse">True when another program has the file open.</param>
/// <param name="IsTransferring">True while the file is uploading or downloading.</param>
/// <param name="IsSystemFile">True for files the provider manages itself, such as desktop.ini.</param>
public sealed record CloudFileState(
    string Path,
    long LogicalBytes,
    long AllocatedBytes,
    DateTime LastAccessUtc,
    DateTime LastWriteUtc,
    bool IsPlaceholder,
    bool IsHydrated,
    bool HasForeignReparsePoint,
    CloudSyncState SyncState,
    long ModifiedBytes,
    CloudPinState PinState,
    bool IsInUse,
    bool IsTransferring,
    bool IsSystemFile);
