using System.Runtime.InteropServices;
using System.Text;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.App.Platforms.Windows;

public sealed class WindowsJunkScanner : IJunkScanner
{
    // Overlap handling (the same Temp folder reached two ways, NuGetScratch nested inside Temp), the age filter,
    // a temp folder that is itself a link (reported as a notice, not scanned),
    // link/junction refusal and the per-file manifest all live in Core's JunkSourceScanner; this class only says
    // where junk lives on Windows.
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
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        // Windows\Prefetch is deliberately absent: Windows manages it, and clearing it only makes the next
        // launch of every app slower while the cache is rebuilt.
        //
        // Windows\Temp is deliberately absent too: any standard user can create folders there, so if this app is
        // run as administrator a planted folder could be swapped for a junction mid-clean and redirect deletes
        // into protected locations. Path-based link checks narrow that race but cannot close it.
        var sources = new List<JunkSource>
        {
            // %TEMP% can come back in 8.3 form (C:\Users\FIRSTN~1\...), which would not match the long path of
            // %LOCALAPPDATA%\Temp and would count the same folder twice — so expand it first.
            new(ToLongPath(Path.GetTempPath()), JunkCategory.TempFiles, "User Temp Files", IsTemp: true),
            new(Path.Combine(localAppData, "Temp"), JunkCategory.TempFiles, "Local App Temp Files", IsTemp: true),
            new(Path.Combine(windowsDir, "SoftwareDistribution", "Download"), JunkCategory.SystemCache, "Windows Update Cache"),
            new(Path.Combine(localAppData, "Microsoft", "Windows", "WER"), JunkCategory.SystemLogs, "Windows Error Reporting"),
            new(Path.Combine(localAppData, "CrashDumps"), JunkCategory.SystemLogs, "Application Crash Dumps"),
            new(Path.Combine(localAppData, "D3DSCache"), JunkCategory.SystemCache, "DirectX Shader Cache"),

            new(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Chrome Cache"),
            new(Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache"), JunkCategory.BrowserCache, "Chrome Code Cache"),
            new(Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Edge Cache"),
            new(Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache"), JunkCategory.BrowserCache, "Edge Code Cache"),
            new(Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache"), JunkCategory.BrowserCache, "Brave Cache"),
            new(Path.Combine(localAppData, "Microsoft", "Windows", "INetCache"), JunkCategory.BrowserCache, "Internet Cache"),

            new(Path.Combine(localAppData, "npm-cache"), JunkCategory.PackageManagerCache, "npm Cache"),
            new(Path.Combine(appData, "npm-cache"), JunkCategory.PackageManagerCache, "npm Cache"),
            new(Path.Combine(localAppData, "pip", "Cache"), JunkCategory.PackageManagerCache, "pip Cache"),
            new(Path.Combine(localAppData, "NuGet", "v3-cache"), JunkCategory.PackageManagerCache, "NuGet HTTP Cache"),

            // Nested inside Temp: listed on its own for the more specific label; Temp's walk skips it, and it
            // inherits Temp's age rules.
            new(Path.Combine(localAppData, "Temp", "NuGetScratch"), JunkCategory.PackageManagerCache, "NuGet Scratch Cache", IsTemp: true),
        };

        // Electron apps keep a Chromium profile; only its three cache folders are listed, never the profile
        // itself (which holds sign-ins and local data).
        AddChromiumCaches(sources, Path.Combine(appData, "Slack"), "Slack");
        AddChromiumCaches(sources, Path.Combine(appData, "discord"), "Discord");
        AddChromiumCaches(sources, Path.Combine(appData, "Microsoft", "Teams"), "Teams (classic)");

        // New Teams is a packaged app whose WebView2 profiles are named WV2Profile_tfw (work) / _tfl (personal).
        string newTeams = Path.Combine(
            localAppData, "Packages", "MSTeams_8wekyb3d8bbwe", "LocalCache", "Microsoft", "MSTeams", "EBWebView");
        if (Directory.Exists(newTeams))
        {
            foreach (string profileDir in Directory.EnumerateDirectories(newTeams, "WV2Profile_*"))
            {
                AddChromiumCaches(sources, profileDir, $"Teams ({Path.GetFileName(profileDir)})");
            }
        }

        sources.AddRange(DeveloperCacheSources.ForWindows(localAppData, appData, userProfile));

        string firefoxProfiles = Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles");
        if (Directory.Exists(firefoxProfiles))
        {
            foreach (string profileDir in Directory.EnumerateDirectories(firefoxProfiles))
            {
                sources.Add(new(
                    Path.Combine(profileDir, "cache2"),
                    JunkCategory.BrowserCache,
                    $"Firefox Cache ({Path.GetFileName(profileDir)})"));
            }
        }

        return sources;
    }

    private static void AddChromiumCaches(List<JunkSource> sources, string profileDir, string app)
    {
        sources.Add(new(Path.Combine(profileDir, "Cache"), JunkCategory.SystemCache, $"{app} Cache"));
        sources.Add(new(Path.Combine(profileDir, "Code Cache"), JunkCategory.SystemCache, $"{app} Code Cache"));
        sources.Add(new(Path.Combine(profileDir, "GPUCache"), JunkCategory.SystemCache, $"{app} GPU Cache"));
    }

    private static string ToLongPath(string path)
    {
        var buffer = new StringBuilder(1024);
        uint length = GetLongPathName(path, buffer, (uint)buffer.Capacity);
        return length > 0 && length < buffer.Capacity ? buffer.ToString() : path;
    }

    [DllImport("kernel32.dll", EntryPoint = "GetLongPathNameW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetLongPathName(string shortPath, StringBuilder longPath, uint bufferLength);
}
