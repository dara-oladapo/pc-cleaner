using System.Runtime.CompilerServices;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.App.Platforms.Windows;

public sealed class WindowsJunkScanner : IJunkScanner
{
    public async IAsyncEnumerable<JunkItem> ScanAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        IProgress<int>? filesScanned = null)
    {
        // Several "distinct" special folders can resolve to the identical physical path (e.g. Path.GetTempPath()
        // and %LOCALAPPDATA%\Temp), so track full paths already reported to avoid double-counting the same bytes.
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
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var sources = new List<(string, JunkCategory, string)>
        {
            (Path.GetTempPath(), JunkCategory.TempFiles, "User Temp Files"),
            (Path.Combine(localAppData, "Temp"), JunkCategory.TempFiles, "Local App Temp Files"),
            (Path.Combine(windowsDir, "Temp"), JunkCategory.TempFiles, "System Temp Files"),
            (Path.Combine(windowsDir, "Prefetch"), JunkCategory.SystemCache, "Windows Prefetch Cache"),
            (Path.Combine(windowsDir, "SoftwareDistribution", "Download"), JunkCategory.SystemCache, "Windows Update Cache"),
            (Path.Combine(localAppData, "Microsoft", "Windows", "WER"), JunkCategory.SystemLogs, "Windows Error Reporting"),
            (Path.Combine(localAppData, "CrashDumps"), JunkCategory.SystemLogs, "Application Crash Dumps"),

            (Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Chrome Cache"),
            (Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"), JunkCategory.BrowserCache, "Chrome Code Cache"),
            (Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Edge Cache"),
            (Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"), JunkCategory.BrowserCache, "Edge Code Cache"),
            (Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Brave Cache"),

            (Path.Combine(localAppData, "npm-cache"), JunkCategory.PackageManagerCache, "npm Cache"),
            (Path.Combine(appData, "npm-cache"), JunkCategory.PackageManagerCache, "npm Cache"),
            (Path.Combine(localAppData, "pip", "Cache"), JunkCategory.PackageManagerCache, "pip Cache"),
            (Path.Combine(localAppData, "NuGet", "v3-cache"), JunkCategory.PackageManagerCache, "NuGet HTTP Cache"),
            (Path.Combine(localAppData, "Temp", "NuGetScratch"), JunkCategory.PackageManagerCache, "NuGet Scratch Cache"),
        };

        string firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (string profileDir in Directory.EnumerateDirectories(firefoxProfiles))
            {
                sources.Add((
                    Path.Combine(profileDir, "cache2"),
                    JunkCategory.BrowserCache,
                    $"Firefox Cache ({Path.GetFileName(profileDir)})"));
            }
        }

        return sources;
    }
}
