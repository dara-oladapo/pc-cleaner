using System.Runtime.CompilerServices;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// The platform-agnostic half of a junk scan: platform scanners supply the list of places, this plans their
/// overlaps (<see cref="JunkRootPlanner"/>), walks each one (<see cref="JunkRootScanner"/>) and yields items that
/// carry the exact file manifest the cleaner will act on.
/// </summary>
public static class JunkSourceScanner
{
    public static async IAsyncEnumerable<JunkItem> ScanAsync(
        IEnumerable<JunkSource> sources,
        StringComparison pathComparison,
        DateTime nowUtc,
        TimeSpan minimumAge,
        IProgress<int>? filesScanned = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        bool includeDeveloperSources = true)
    {
        foreach (PlannedJunkRoot root in JunkRootPlanner.Plan(sources, pathComparison, includeDeveloperSources))
        {
            cancellationToken.ThrowIfCancellationRequested();

            JunkRootScan scan = await JunkRootScanner
                .ScanAsync(root, nowUtc, minimumAge, pathComparison, cancellationToken, filesScanned)
                .ConfigureAwait(false);

            if (scan.RootIsLink)
            {
                // Following the link could clean another drive or folder entirely; tell the user instead of
                // silently showing nothing.
                yield return new JunkItem(root.Path, 0, root.Category, root.Description, IsDirectory: true)
                {
                    Notice = $"{root.Description} was not scanned: {root.Path} is a link to another location.",
                };
            }
            else if (scan.TotalBytes > 0)
            {
                yield return new JunkItem(
                    root.Path,
                    scan.TotalBytes,
                    root.Category,
                    $"{root.Description} ({scan.Files.Count:N0} items)",
                    IsDirectory: true)
                {
                    Files = scan.Files,
                    Directories = scan.Directories,
                    MinimumAge = root.IsTemp ? minimumAge : null,
                    SelectedByDefault = root.SelectedByDefault,
                };
            }
        }
    }
}
