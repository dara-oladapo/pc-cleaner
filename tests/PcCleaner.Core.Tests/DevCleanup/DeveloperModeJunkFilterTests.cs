using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests.DevCleanup;

/// <summary>
/// With developer mode off, developer caches are left out of the junk scan, but their folders must still be
/// skipped by any parent root. On macOS "User Caches" (~/Library/Caches) contains Playwright, JetBrains,
/// go-build and CocoaPods; if those sources were simply removed, the parent would re-absorb them pre-ticked.
/// </summary>
public class DeveloperModeJunkFilterTests
{
    private static readonly DateTime FutureNow = DateTime.UtcNow.AddDays(10);

    private static List<JunkSource> MacLikeSources(string caches) =>
    [
        new(caches, JunkCategory.SystemCache, "User Caches"),
        new(Path.Combine(caches, "ms-playwright"), JunkCategory.DeveloperCache, "Playwright browsers", SelectedByDefault: false, RequiresDeveloperMode: true),
        new(Path.Combine(caches, "go-build"), JunkCategory.DeveloperCache, "Go build cache", RequiresDeveloperMode: true),
    ];

    [Fact]
    public void Plan_DeveloperModeOff_DropsDeveloperSources_ButParentStillExcludesThem()
    {
        string caches = JunkRootPlanner.Normalize(Path.Combine(Path.GetTempPath(), "planner-" + Guid.NewGuid().ToString("N"), "Caches"));

        var plan = JunkRootPlanner.Plan(MacLikeSources(caches), StringComparison.Ordinal, includeDeveloperSources: false);

        PlannedJunkRoot parent = Assert.Single(plan);
        Assert.Equal(caches, parent.Path);
        Assert.Contains(Path.Combine(caches, "ms-playwright"), parent.ExcludedPaths);
        Assert.Contains(Path.Combine(caches, "go-build"), parent.ExcludedPaths);
    }

    [Fact]
    public void Plan_DeveloperModeOn_PlansDeveloperSourcesAsTheirOwnRoots()
    {
        string caches = JunkRootPlanner.Normalize(Path.Combine(Path.GetTempPath(), "planner-" + Guid.NewGuid().ToString("N"), "Caches"));

        var plan = JunkRootPlanner.Plan(MacLikeSources(caches), StringComparison.Ordinal, includeDeveloperSources: true);

        Assert.Equal(3, plan.Count);
    }

    [Fact]
    public void Plan_FolderAlsoNamedByANonDeveloperSource_IsStillScanned()
    {
        string root = JunkRootPlanner.Normalize(Path.Combine(Path.GetTempPath(), "planner-" + Guid.NewGuid().ToString("N"), "npm-cache"));

        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(root, JunkCategory.DeveloperCache, "npm (dev)", RequiresDeveloperMode: true),
                new JunkSource(root, JunkCategory.PackageManagerCache, "npm cache"),
            ],
            StringComparison.Ordinal,
            includeDeveloperSources: false);

        Assert.Single(plan);
    }

    [Fact]
    public async Task Scan_DeveloperModeOff_NeverOffersDeveloperCacheFiles_EvenUnderTheParent()
    {
        using var dir = new TestDirectory();
        string caches = Path.Combine(dir.Path, "Caches");
        dir.WriteFileOfSize("Caches/com.example.app/cache.db", 100);
        string playwright = dir.WriteFileOfSize("Caches/ms-playwright/chromium-1200/chrome.exe", 5000);
        string goBuild = dir.WriteFileOfSize("Caches/go-build/aa/obj", 300);

        var items = new List<JunkItem>();
        await foreach (JunkItem item in JunkSourceScanner.ScanAsync(
            MacLikeSources(caches), JunkRootPlanner.DefaultPathComparison, FutureNow, JunkSafetyRules.DefaultMinimumAge,
            includeDeveloperSources: false))
        {
            items.Add(item);
        }

        JunkItem userCaches = Assert.Single(items);
        Assert.Equal("User Caches (1 items)", userCaches.Description);
        Assert.Equal(100, userCaches.SizeBytes);
        Assert.DoesNotContain(userCaches.Files!, f => f.Path == playwright || f.Path == goBuild);
    }

    [Fact]
    public async Task Scan_DeveloperModeOn_OffersDeveloperCachesSeparately()
    {
        using var dir = new TestDirectory();
        string caches = Path.Combine(dir.Path, "Caches");
        dir.WriteFileOfSize("Caches/com.example.app/cache.db", 100);
        dir.WriteFileOfSize("Caches/ms-playwright/chromium-1200/chrome.exe", 5000);

        var items = new List<JunkItem>();
        await foreach (JunkItem item in JunkSourceScanner.ScanAsync(
            MacLikeSources(caches), JunkRootPlanner.DefaultPathComparison, FutureNow, JunkSafetyRules.DefaultMinimumAge))
        {
            items.Add(item);
        }

        Assert.Equal(2, items.Count);
        JunkItem playwrightItem = items.Single(i => i.Category == JunkCategory.DeveloperCache);
        Assert.False(playwrightItem.SelectedByDefault);
        Assert.Equal(100, items.Single(i => i.Category == JunkCategory.SystemCache).SizeBytes);
    }
}
