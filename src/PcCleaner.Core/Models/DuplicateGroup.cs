namespace PcCleaner.Core.Models;

public sealed record DuplicateGroup(string ContentHash, long SizeBytesEach, IReadOnlyList<FileEntry> Files)
{
    public long TotalSizeBytes => SizeBytesEach * Files.Count;

    public long ReclaimableBytes => SizeBytesEach * (Files.Count - 1);
}
