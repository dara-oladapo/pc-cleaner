using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.App.Platforms.MacCatalyst;

public sealed class MacJunkScanner : IJunkScanner
{
    // Overlap handling (browser, Homebrew and pip caches all live inside ~/Library/Caches), symlink refusal and
    // the per-file manifest all live in Core's JunkSourceScanner; this class only says where junk lives on macOS.
    // Case-insensitive because APFS is by default.
    public IAsyncEnumerable<JunkItem> ScanAsync(
        CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null) =>
        JunkSourceScanner.ScanAsync(
            GetJunkSources(),
            StringComparison.OrdinalIgnoreCase,
            DateTime.UtcNow,
            JunkSafetyRules.DefaultMinimumAge,
            filesScanned,
            cancellationToken);

    private static List<JunkSource> GetJunkSources()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string library = Path.Combine(home, "Library");

        // The specific caches nested in ~/Library/Caches are listed for their labels; "User Caches" then counts
        // only what none of them covers, so nothing is offered twice.
        return
        [
            new(Path.Combine(library, "Caches"), JunkCategory.SystemCache, "User Caches"),
            new(Path.Combine(library, "Logs"), JunkCategory.SystemLogs, "User Logs"),
            new(Path.Combine(home, ".Trash"), JunkCategory.TrashBin, "User Trash"),

            new(Path.Combine(library, "Caches", "com.apple.Safari"), JunkCategory.BrowserCache, "Safari Cache"),
            new(Path.Combine(library, "Caches", "Google", "Chrome"), JunkCategory.BrowserCache, "Chrome Cache"),
            new(Path.Combine(library, "Caches", "Firefox"), JunkCategory.BrowserCache, "Firefox Cache"),
            new(Path.Combine(library, "Caches", "BraveSoftware"), JunkCategory.BrowserCache, "Brave Cache"),

            new(Path.Combine(library, "Developer", "Xcode", "DerivedData"), JunkCategory.SystemCache, "Xcode DerivedData"),
            // Xcode Archives is deliberately absent: it holds the dSYMs for shipped builds, which cannot be
            // regenerated, so a permanent delete is never right. DerivedData is pure build output and stays.
            new(Path.Combine(library, "Developer", "CoreSimulator", "Caches"), JunkCategory.SystemCache, "iOS Simulator Caches"),

            new(Path.Combine(library, "Caches", "Homebrew"), JunkCategory.PackageManagerCache, "Homebrew Cache"),
            new(Path.Combine(home, ".npm", "_cacache"), JunkCategory.PackageManagerCache, "npm Cache"),
            new(Path.Combine(library, "Caches", "pip"), JunkCategory.PackageManagerCache, "pip Cache"),
            new(Path.Combine(home, ".cache", "pip"), JunkCategory.PackageManagerCache, "pip Cache"),
        ];
    }
}
