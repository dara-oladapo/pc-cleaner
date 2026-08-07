using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.Platforms.MacCatalyst;

public sealed class MacJunkScanner : IJunkScanner
{
    public async IAsyncEnumerable<JunkItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        // Some candidate paths overlap (e.g. pip's cache dir varies by version/config), so track full paths
        // already reported to avoid double-counting the same bytes under two different labels.
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string path, JunkCategory category, string description) in GetJunkSources())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(path))
            {
                continue;
            }

            string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            if (!seenPaths.Add(fullPath))
            {
                continue;
            }

            (long totalBytes, int fileCount) = await FileSystemWalker.ComputeStatsAsync(path, cancellationToken, filesScanned);
            if (totalBytes > 0)
            {
                yield return new JunkItem(path, totalBytes, category, $"{description} ({fileCount:N0} items)", IsDirectory: true);
            }
        }
    }

    private static List<(string Path, JunkCategory Category, string Description)> GetJunkSources()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string library = Path.Combine(home, "Library");

        return
        [
            (Path.Combine(library, "Caches"), JunkCategory.SystemCache, "User Caches"),
            (Path.Combine(library, "Logs"), JunkCategory.SystemLogs, "User Logs"),
            (Path.Combine(home, ".Trash"), JunkCategory.TrashBin, "User Trash"),

            (Path.Combine(library, "Caches", "com.apple.Safari"), JunkCategory.BrowserCache, "Safari Cache"),
            (Path.Combine(library, "Caches", "Google", "Chrome"), JunkCategory.BrowserCache, "Chrome Cache"),
            (Path.Combine(library, "Caches", "Firefox"), JunkCategory.BrowserCache, "Firefox Cache"),
            (Path.Combine(library, "Caches", "BraveSoftware"), JunkCategory.BrowserCache, "Brave Cache"),

            (Path.Combine(library, "Developer", "Xcode", "DerivedData"), JunkCategory.SystemCache, "Xcode DerivedData"),
            (Path.Combine(library, "Developer", "Xcode", "Archives"), JunkCategory.SystemCache, "Xcode Archives"),
            (Path.Combine(library, "Developer", "CoreSimulator", "Caches"), JunkCategory.SystemCache, "iOS Simulator Caches"),

            (Path.Combine(library, "Caches", "Homebrew"), JunkCategory.PackageManagerCache, "Homebrew Cache"),
            (Path.Combine(home, ".npm", "_cacache"), JunkCategory.PackageManagerCache, "npm Cache"),
            (Path.Combine(library, "Caches", "pip"), JunkCategory.PackageManagerCache, "pip Cache"),
            (Path.Combine(home, ".cache", "pip"), JunkCategory.PackageManagerCache, "pip Cache"),
        ];
    }
}
