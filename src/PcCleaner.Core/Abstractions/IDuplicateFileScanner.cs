using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

public interface IDuplicateFileScanner
{
    IAsyncEnumerable<DuplicateGroup> ScanAsync(
        IReadOnlyList<string> rootPaths,
        long minFileSizeBytes = 1,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null);
}
