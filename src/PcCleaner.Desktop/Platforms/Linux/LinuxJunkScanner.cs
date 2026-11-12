using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>Stub until #10. Finds nothing and reads nothing, so a scan completes with "No junk found".</summary>
/// <remarks>
/// An empty result rather than <see cref="NotImplementedException"/>: the scan command awaits this inside an
/// async relay command, and an exception there would take the whole app down instead of just this screen.
/// </remarks>
public sealed class LinuxJunkScanner : IJunkScanner
{
    public async IAsyncEnumerable<JunkItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        await Task.CompletedTask;
        yield break;
    }
}
