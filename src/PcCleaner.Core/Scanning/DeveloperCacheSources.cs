using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Where developer tools keep their caches, on each OS. Pure path building (plus one directory listing for the
/// per-version JetBrains folders), so it lives in Core where it can be tested; the platform scanners pass in the
/// real folders.
/// <para>
/// Every source here is flagged <see cref="JunkSource.RequiresDeveloperMode"/>. Large, slow-to-rebuild stores
/// (installed packages, downloaded toolchains and browsers, device symbols, IDE indexes) are listed but not
/// pre-selected; cheap caches are.
/// </para>
/// <para>
/// Deliberately absent, because they are user state rather than caches: the Go module cache (read-only files,
/// cleared with <c>go clean -modcache</c>), Maven's <c>~/.m2</c>, Yarn Berry's global cache (Plug'n'Play projects
/// run straight from it), Ollama models, Android emulator images, Docker data and VM disks.
/// </para>
/// </summary>
public static class DeveloperCacheSources
{
    public static List<JunkSource> ForWindows(string localAppData, string appData, string userProfile)
    {
        var sources = new List<JunkSource>
        {
            Cheap(Path.Combine(localAppData, "Yarn", "Cache"), "Yarn Cache"),
            OptIn(Path.Combine(localAppData, "pnpm", "store"), "pnpm Store"),
            Cheap(Path.Combine(localAppData, "go-build"), "Go Build Cache"),
            OptIn(Path.Combine(userProfile, ".nuget", "packages"), "NuGet Global Packages"),
            OptIn(Path.Combine(localAppData, "ms-playwright"), "Playwright Browsers"),
        };

        sources.AddRange(Gradle(userProfile));
        sources.AddRange(Cargo(userProfile));
        sources.AddRange(VsCode(Path.Combine(appData, "Code")));

        // One folder per product and version (Rider2025.2, IntelliJIdea2025.3, ...). Only their caches and logs.
        // Toolbox is skipped outright: it lives in the same parent and holds the installed IDEs themselves.
        string jetBrains = Path.Combine(localAppData, "JetBrains");
        if (Directory.Exists(jetBrains))
        {
            foreach (string productDir in Directory.EnumerateDirectories(jetBrains).Order(StringComparer.Ordinal))
            {
                string product = Path.GetFileName(productDir);
                if (string.Equals(product, "Toolbox", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sources.Add(OptIn(Path.Combine(productDir, "caches"), $"JetBrains Caches ({product})"));
                sources.Add(Cheap(Path.Combine(productDir, "log"), $"JetBrains Logs ({product})"));
            }
        }

        return sources;
    }

    public static List<JunkSource> ForMac(string home)
    {
        string library = Path.Combine(home, "Library");
        string caches = Path.Combine(library, "Caches");
        string deviceSupport = Path.Combine(library, "Developer", "Xcode");

        var sources = new List<JunkSource>
        {
            Cheap(Path.Combine(caches, "Yarn"), "Yarn Cache"),
            OptIn(Path.Combine(library, "pnpm", "store"), "pnpm Store"),
            Cheap(Path.Combine(caches, "go-build"), "Go Build Cache"),
            Cheap(Path.Combine(caches, "CocoaPods"), "CocoaPods Cache"),
            OptIn(Path.Combine(deviceSupport, "iOS DeviceSupport"), "Xcode iOS DeviceSupport"),
            OptIn(Path.Combine(deviceSupport, "watchOS DeviceSupport"), "Xcode watchOS DeviceSupport"),
            OptIn(Path.Combine(deviceSupport, "tvOS DeviceSupport"), "Xcode tvOS DeviceSupport"),
            OptIn(Path.Combine(home, ".nuget", "packages"), "NuGet Global Packages"),
            OptIn(Path.Combine(caches, "JetBrains"), "JetBrains Caches"),
            OptIn(Path.Combine(caches, "ms-playwright"), "Playwright Browsers"),
        };

        sources.AddRange(Gradle(home));
        sources.AddRange(Cargo(home));
        sources.AddRange(VsCode(Path.Combine(library, "Application Support", "Code")));
        return sources;
    }

    private static IEnumerable<JunkSource> Gradle(string home)
    {
        string gradle = Path.Combine(home, ".gradle");
        yield return OptIn(Path.Combine(gradle, "caches"), "Gradle Caches");
        yield return OptIn(Path.Combine(gradle, "wrapper", "dists"), "Gradle Wrapper Distributions");
    }

    private static IEnumerable<JunkSource> Cargo(string home)
    {
        string cargo = Path.Combine(home, ".cargo");
        yield return OptIn(Path.Combine(cargo, "registry", "cache"), "Cargo Registry Cache");
        yield return OptIn(Path.Combine(cargo, "registry", "src"), "Cargo Registry Sources");
        yield return OptIn(Path.Combine(cargo, "git", "checkouts"), "Cargo Git Checkouts");
    }

    // Settings, extensions and workspace state live alongside these in the same folder, so only the named
    // cache folders are listed, never the folder itself.
    private static IEnumerable<JunkSource> VsCode(string codeDir)
    {
        yield return Cheap(Path.Combine(codeDir, "Cache"), "VS Code Cache");
        yield return Cheap(Path.Combine(codeDir, "CachedData"), "VS Code Cached Data");
        yield return Cheap(Path.Combine(codeDir, "CachedExtensionVSIXs"), "VS Code Cached Extension Downloads");
        yield return Cheap(Path.Combine(codeDir, "Code Cache"), "VS Code Code Cache");
        yield return Cheap(Path.Combine(codeDir, "GPUCache"), "VS Code GPU Cache");
        yield return Cheap(Path.Combine(codeDir, "logs"), "VS Code Logs");
    }

    private static JunkSource Cheap(string path, string description) =>
        new(path, JunkCategory.DeveloperCache, description, RequiresDeveloperMode: true);

    private static JunkSource OptIn(string path, string description) =>
        new(path, JunkCategory.DeveloperCache, description, SelectedByDefault: false, RequiresDeveloperMode: true);
}
