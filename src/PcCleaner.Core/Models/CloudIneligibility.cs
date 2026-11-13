namespace PcCleaner.Core.Models;

/// <summary>
/// Why a local file in a cloud folder may not be returned to online-only. <see cref="None"/> means it may.
/// </summary>
/// <remarks>
/// The order is the order the rules are checked in, so a file reports the most basic reason first.
/// </remarks>
public enum CloudIneligibility
{
    None,
    NotPlaceholder,
    ForeignReparsePoint,
    NotHydrated,
    SystemFile,
    Excluded,
    Pinned,
    NotInSync,
    LocallyModified,
    Transferring,
    RecentlyUsed,
    InUse,

    /// <summary>Release only: the path is not inside a root that allows release. Never produced by a scan.</summary>
    OutsideReleasableRoot,
}
