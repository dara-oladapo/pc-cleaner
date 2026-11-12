namespace PcCleaner.Core.Models;

public sealed record JunkItem(
    string Path,
    long SizeBytes,
    JunkCategory Category,
    string Description,
    bool IsDirectory)
{
    /// <summary>
    /// For a directory item: the exact files the scan counted. The cleaner deletes these and nothing else — it
    /// never re-walks the folder. A directory item without this manifest is refused, not cleaned.
    /// </summary>
    public IReadOnlyList<JunkFileEntry>? Files { get; init; }

    /// <summary>For a directory item: the sub-folders the scan entered, which the cleaner may remove once empty.</summary>
    public IReadOnlyList<JunkDirectoryEntry>? Directories { get; init; }

    /// <summary>
    /// Set for temp roots: the minimum age the scan applied. The cleaner re-applies it, with fresh timestamps and
    /// the current time, immediately before each delete.
    /// </summary>
    public TimeSpan? MinimumAge { get; init; }

    /// <summary>
    /// When set, this is not a cleanable item but a note for the user about a location that was deliberately
    /// not scanned (for example a temp folder that is itself a link to somewhere else).
    /// </summary>
    public string? Notice { get; init; }
}

/// <summary>A file as the scan saw it. The cleaner deletes it only if all three facts still match.</summary>
public sealed record JunkFileEntry(string Path, long SizeBytes, DateTime LastWriteUtc, DateTime CreationUtc);

/// <summary>A folder as the scan saw it. A different creation time at clean time means it was recreated.</summary>
public sealed record JunkDirectoryEntry(string Path, DateTime CreationUtc);
