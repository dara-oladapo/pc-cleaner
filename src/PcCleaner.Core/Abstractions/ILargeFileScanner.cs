using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

public interface ILargeFileScanner
{
    IAsyncEnumerable<FileEntry> ScanAsync(
        IReadOnlyList<string> rootPaths,
        long minSizeBytes,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null);
}
