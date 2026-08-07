namespace PcCleaner.Core.Models;

public sealed record JunkItem(
    string Path,
    long SizeBytes,
    JunkCategory Category,
    string Description,
    bool IsDirectory);
