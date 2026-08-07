using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Platform-specific: knows where junk lives on this OS (temp dirs, browser caches, package manager caches, logs).</summary>
public interface IJunkScanner
{
    /// <summary>filesScanned reports the running file count within whatever source is currently being sized — resets per source.</summary>
    IAsyncEnumerable<JunkItem> ScanAsync(CancellationToken cancellationToken = default, IProgress<int>? filesScanned = null);
}
