namespace PcCleaner.Core.Scanning;

/// <summary>
/// The pure decisions about what junk cleaning may touch. Junk cleaning is a permanent delete, so every rule
/// here errs towards leaving a file alone. No file-system access: callers pass in names and timestamps.
/// </summary>
public static class JunkSafetyRules
{
    /// <summary>
    /// How old a file (or folder) in a temp root must be before it counts as junk. The single place this
    /// threshold is defined. Two days keeps anything a running program, an installer or an in-progress
    /// download is still using.
    /// </summary>
    public static readonly TimeSpan DefaultMinimumAge = TimeSpan.FromDays(2);

    /// <summary>Directory names that are never entered when they sit directly under a temp root.</summary>
    private static readonly string[] ProtectedTempDirectoryNames = ["claude"];

    /// <summary>
    /// File extensions that are never junk anywhere: virtual disks (WSL, Docker, Hyper-V and dev drives keep
    /// these in temp-like places, and deleting one destroys a whole machine image).
    /// </summary>
    private static readonly string[] ProtectedExtensions = [".vhdx", ".vhd", ".avhdx"];

    public static bool IsProtectedFileName(string name) =>
        ProtectedExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The timestamp age is measured from: the newer of last-write and creation. Creation matters because
    /// extracting an archive or copying a file keeps the original (possibly years-old) write time, while the
    /// program using it was started minutes ago.
    /// </summary>
    public static DateTime AgeTimestampUtc(DateTime lastWriteUtc, DateTime creationUtc) =>
        lastWriteUtc > creationUtc ? lastWriteUtc : creationUtc;

    public static bool IsOldEnough(DateTime lastWriteUtc, DateTime creationUtc, DateTime nowUtc, TimeSpan minimumAge) =>
        nowUtc - AgeTimestampUtc(lastWriteUtc, creationUtc) >= minimumAge;

    /// <summary>True when a file found during a junk walk must not be counted or deleted.</summary>
    public static bool ShouldSkipFile(
        string name,
        bool inTempRoot,
        DateTime lastWriteUtc,
        DateTime creationUtc,
        DateTime nowUtc,
        TimeSpan minimumAge)
    {
        if (IsProtectedFileName(name))
        {
            return true;
        }

        return inTempRoot && !IsOldEnough(lastWriteUtc, creationUtc, nowUtc, minimumAge);
    }

    /// <summary>True when a sub-directory found during a junk walk must not be entered (nor removed).</summary>
    public static bool ShouldSkipDirectory(
        string name,
        bool inTempRoot,
        bool isDirectChildOfRoot,
        DateTime lastWriteUtc,
        DateTime creationUtc,
        DateTime nowUtc,
        TimeSpan minimumAge)
    {
        if (IsProtectedFileName(name))
        {
            return true;
        }

        if (!inTempRoot)
        {
            return false;
        }

        if (isDirectChildOfRoot
            && ProtectedTempDirectoryNames.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !IsOldEnough(lastWriteUtc, creationUtc, nowUtc, minimumAge);
    }
}
