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
    public IReadOnlyList<FileEntry>? Files { get; init; }

    /// <summary>For a directory item: the sub-folders the scan entered, which the cleaner may remove once empty.</summary>
    public IReadOnlyList<string>? Directories { get; init; }
}
