using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

/// <summary>
/// Every test works inside its own <see cref="TestDirectory"/>. Instead of back-dating files (creation time cannot
/// be set on Linux), the scan's clock is moved forward: everything the test creates is "old", and a file is made
/// "recent" by setting its write time just before that future clock.
/// </summary>
public class JunkRootScannerTests
{
    private static readonly TimeSpan MinAge = JunkSafetyRules.DefaultMinimumAge;
    private static readonly DateTime FutureNow = DateTime.UtcNow.AddDays(10);
    private static readonly StringComparison Cmp = JunkRootPlanner.DefaultPathComparison;

    private static PlannedJunkRoot TempRoot(string path, params string[] excluded) =>
        new(JunkRootPlanner.Normalize(path), JunkCategory.TempFiles, "Temp", IsTemp: true, excluded.Select(JunkRootPlanner.Normalize).ToList());

    private static HashSet<string> Names(JunkRootScan scan) => scan.Files.Select(f => Path.GetFileName(f.Path)).ToHashSet();

    [Fact]
    public void Scan_TempRoot_CountsOnlyOldFiles()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("old.tmp", 10);
        string recent = dir.WriteFileOfSize("recent.tmp", 20);
        File.SetLastWriteTimeUtc(recent, FutureNow.AddHours(-1));

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(dir.Path), FutureNow, MinAge, Cmp);

        Assert.Equal(["old.tmp"], Names(scan));
        Assert.Equal(10, scan.TotalBytes);
    }

    [Fact]
    public void Scan_NonTempRoot_IgnoresAge()
    {
        using var dir = new TestDirectory();
        string recent = dir.WriteFileOfSize("recent.bin", 20);
        File.SetLastWriteTimeUtc(recent, FutureNow.AddHours(-1));
        var root = new PlannedJunkRoot(dir.Path, JunkCategory.BrowserCache, "Cache", IsTemp: false, []);

        Assert.Single(JunkRootScanner.Scan(root, FutureNow, MinAge, Cmp).Files);
    }

    [Fact]
    public void Scan_NeverCountsVirtualDisks()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("wsl/ext4.vhdx", 10);
        dir.WriteFileOfSize("a.VHD", 10);
        dir.WriteFileOfSize("b.avhdx", 10);
        dir.WriteFileOfSize("junk.tmp", 1);

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(dir.Path), FutureNow, MinAge, Cmp);

        Assert.Equal(["junk.tmp"], Names(scan));
    }

    [Fact]
    public void Scan_SkipsClaudeDirectoryDirectlyUnderTemp_ButNotDeeper()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("claude/session/state.json", 10);
        dir.WriteFileOfSize("other/claude/deep.tmp", 5);

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(dir.Path), FutureNow, MinAge, Cmp);

        Assert.Equal(["deep.tmp"], Names(scan));
        Assert.DoesNotContain(scan.Directories, d => Path.GetFileName(d.Path) == "session");
    }

    [Fact]
    public void Scan_SkipsRecentlyModifiedDirectoriesEntirely()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("active/old-inside.tmp", 10);
        dir.WriteFileOfSize("stale/old.tmp", 10);
        Directory.SetLastWriteTimeUtc(Path.Combine(dir.Path, "active"), FutureNow.AddHours(-1));

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(dir.Path), FutureNow, MinAge, Cmp);

        Assert.Equal(["old.tmp"], Names(scan));
        Assert.DoesNotContain(scan.Directories, d => Path.GetFileName(d.Path) == "active");
    }

    [Fact]
    public void Scan_DoesNotEnterExcludedNestedRoot()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("a.tmp", 10);
        dir.WriteFileOfSize("NuGetScratch/b.tmp", 20);

        JunkRootScan scan = JunkRootScanner.Scan(
            TempRoot(dir.Path, Path.Combine(dir.Path, "NuGetScratch")), FutureNow, MinAge, Cmp);

        Assert.Equal(["a.tmp"], Names(scan));
    }

    [Fact]
    public void Scan_DoesNotFollowDirectoryLinks()
    {
        using var dir = new TestDirectory();
        string outside = Path.Combine(dir.Path, "outside");
        dir.WriteFileOfSize("outside/precious.dat", 100);
        dir.WriteFileOfSize("root/junk.tmp", 1);
        string root = Path.Combine(dir.Path, "root");
        if (!TestLinks.TryCreateDirectoryLink(Path.Combine(root, "link"), outside))
        {
            return;
        }

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(root), FutureNow, MinAge, Cmp);

        Assert.Equal(["junk.tmp"], Names(scan));
        Assert.Empty(scan.Directories);
    }

    [Fact]
    public void Scan_RootThatIsItselfALink_ScansNothing()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("real/file.tmp", 1);
        string link = Path.Combine(dir.Path, "link");
        if (!TestLinks.TryCreateDirectoryLink(link, Path.Combine(dir.Path, "real")))
        {
            return;
        }

        JunkRootScan scan = JunkRootScanner.Scan(TempRoot(link), FutureNow, MinAge, Cmp);
        Assert.Empty(scan.Files);
        Assert.True(scan.RootIsLink);
    }

    [Fact]
    public async Task JunkSourceScanner_RootThatIsALink_YieldsNoticeNotCleanableItem()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("real/file.tmp", 1);
        string link = Path.Combine(dir.Path, "link");
        if (!TestLinks.TryCreateDirectoryLink(link, Path.Combine(dir.Path, "real")))
        {
            return;
        }

        var items = new List<JunkItem>();
        await foreach (JunkItem item in JunkSourceScanner.ScanAsync(
                           [new JunkSource(link, JunkCategory.TempFiles, "User Temp Files", IsTemp: true)], Cmp, FutureNow, MinAge))
        {
            items.Add(item);
        }

        JunkItem notice = Assert.Single(items);
        Assert.NotNull(notice.Notice);
        Assert.Null(notice.Files);
        Assert.Equal(0, notice.SizeBytes);
    }

    [Fact]
    public void Scan_RecordsSizeWriteAndCreationTimes()
    {
        using var dir = new TestDirectory();
        string file = dir.WriteFileOfSize("a.bin", 7);
        var root = new PlannedJunkRoot(JunkRootPlanner.Normalize(dir.Path), JunkCategory.SystemCache, "Cache", IsTemp: false, []);

        JunkFileEntry entry = Assert.Single(JunkRootScanner.Scan(root, FutureNow, MinAge, Cmp).Files);

        Assert.Equal(7, entry.SizeBytes);
        Assert.Equal(File.GetLastWriteTimeUtc(file), entry.LastWriteUtc);
        Assert.Equal(File.GetCreationTimeUtc(file), entry.CreationUtc);
    }

    [Fact]
    public async Task JunkSourceScanner_NestedRoots_CountEachFileOnce()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("Caches/loose.bin", 10);
        dir.WriteFileOfSize("Caches/Homebrew/bottle.tar", 100);
        dir.WriteFileOfSize("Caches/pip/http/wheel.whl", 1000);

        JunkSource[] sources =
        [
            new(Path.Combine(dir.Path, "Caches"), JunkCategory.SystemCache, "User Caches"),
            new(Path.Combine(dir.Path, "Caches", "Homebrew"), JunkCategory.PackageManagerCache, "Homebrew Cache"),
            new(Path.Combine(dir.Path, "Caches", "pip"), JunkCategory.PackageManagerCache, "pip Cache"),
            new(Path.Combine(dir.Path, "Caches", "pip") + Path.DirectorySeparatorChar, JunkCategory.PackageManagerCache, "pip again"),
        ];

        var items = new List<JunkItem>();
        await foreach (JunkItem item in JunkSourceScanner.ScanAsync(sources, Cmp, FutureNow, MinAge))
        {
            items.Add(item);
        }

        Assert.Equal(1110, items.Sum(i => i.SizeBytes));
        Assert.Equal(3, items.SelectMany(i => i.Files!).Select(f => f.Path).Distinct().Count());
        Assert.Equal(10, items.Single(i => i.Category == JunkCategory.SystemCache).SizeBytes);
        Assert.Equal(1000, items.Single(i => i.Description.StartsWith("pip Cache")).SizeBytes);
    }

    [Fact]
    public async Task JunkSourceScanner_ItemsCarrySelectedByDefault()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("Caches/loose.bin", 10);
        dir.WriteFileOfSize("Caches/ms-playwright/chromium/chrome.exe", 100);

        JunkSource[] sources =
        [
            new(Path.Combine(dir.Path, "Caches"), JunkCategory.SystemCache, "User Caches"),
            new(Path.Combine(dir.Path, "Caches", "ms-playwright"), JunkCategory.DeveloperCache, "Playwright", SelectedByDefault: false),
        ];

        var items = new List<JunkItem>();
        await foreach (JunkItem item in JunkSourceScanner.ScanAsync(sources, Cmp, FutureNow, MinAge))
        {
            items.Add(item);
        }

        Assert.True(items.Single(i => i.Category == JunkCategory.SystemCache).SelectedByDefault);
        JunkItem playwright = items.Single(i => i.Category == JunkCategory.DeveloperCache);
        Assert.False(playwright.SelectedByDefault);
        Assert.Equal(100, playwright.SizeBytes);
    }
}
