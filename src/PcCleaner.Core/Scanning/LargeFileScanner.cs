using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Scanning;

/// <summary>Pure System.IO recursive scan — identical behavior on every OS, so it lives in Core rather than a platform project.</summary>
public sealed class LargeFileScanner : ILargeFileScanner
{
    public async IAsyncEnumerable<FileEntry> ScanAsync(
        IReadOnlyList<string> rootPaths,
        long minSizeBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        foreach (string root in rootPaths)
        {
            await foreach (FileEntry entry in FileSystemWalker.EnumerateFilesAsync(root, cancellationToken, filesScanned))
            {
                if (entry.SizeBytes >= minSizeBytes)
                {
                    yield return entry;
                }
            }
        }
    }
}
