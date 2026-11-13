using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class DeveloperCacheSourcesTests
{
    // Absolute bases valid on every OS. Only the JetBrains test creates anything, and only inside a TestDirectory.
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "devcache-" + Guid.NewGuid().ToString("N"));
    private static readonly string LocalAppData = Path.Combine(Base, "Local");
    private static readonly string AppData = Path.Combine(Base, "Roaming");
    private static readonly string Home = Path.Combine(Base, "home");

    private static readonly string[] WindowsOptIn =
    [
        "pnpm Store", "NuGet Global Packages", "Playwright Browsers", "Gradle Caches", "Gradle Wrapper Distributions",
        "Cargo Registry Cache", "Cargo Registry Sources", "Cargo Git Checkouts",
    ];

    private static readonly string[] MacOptIn =
    [
        .. WindowsOptIn, "Xcode iOS DeviceSupport", "Xcode watchOS DeviceSupport", "Xcode tvOS DeviceSupport", "JetBrains Caches",
    ];

    [Fact]
    public void Windows_EverySourceRequiresDeveloperModeAndIsADeveloperCache()
    {
        var sources = DeveloperCacheSources.ForWindows(LocalAppData, AppData, Home);

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.True(s.RequiresDeveloperMode, s.Description));
        Assert.All(sources, s => Assert.Equal(JunkCategory.DeveloperCache, s.Category));
        Assert.All(sources, s => Assert.False(s.IsTemp));
    }

    [Fact]
    public void Mac_EverySourceRequiresDeveloperModeAndIsADeveloperCache()
    {
        var sources = DeveloperCacheSources.ForMac(Home);

        Assert.NotEmpty(sources);
        Assert.All(sources, s => Assert.True(s.RequiresDeveloperMode, s.Description));
        Assert.All(sources, s => Assert.Equal(JunkCategory.DeveloperCache, s.Category));
    }

    [Fact]
    public void Windows_LargeStoresAreListedButNotPreselected()
    {
        var sources = DeveloperCacheSources.ForWindows(LocalAppData, AppData, Home);

        Assert.Equal(
            WindowsOptIn.Order(),
            sources.Where(s => !s.SelectedByDefault).Select(s => s.Description).Order());
        Assert.Contains(sources, s => s.Description == "Yarn Cache" && s.SelectedByDefault);
        Assert.Contains(sources, s => s.Description == "Go Build Cache" && s.SelectedByDefault);
        Assert.Contains(sources, s => s.Description == "VS Code Logs" && s.SelectedByDefault);
    }

    [Fact]
    public void Mac_LargeStoresAreListedButNotPreselected()
    {
        var sources = DeveloperCacheSources.ForMac(Home);

        Assert.Equal(
            MacOptIn.Order(),
            sources.Where(s => !s.SelectedByDefault).Select(s => s.Description).Order());
        Assert.Contains(sources, s => s.Description == "CocoaPods Cache" && s.SelectedByDefault);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void NeverListsUserState(bool windows)
    {
        var sources = windows ? DeveloperCacheSources.ForWindows(LocalAppData, AppData, Home) : DeveloperCacheSources.ForMac(Home);
        string[] forbidden =
        [
            Path.Combine(Home, "go", "pkg", "mod"),
            Path.Combine(Home, ".m2"),
            Path.Combine(Home, ".ollama"),
            Path.Combine(Home, ".android"),
            Path.Combine(Home, ".claude"),
            Path.Combine(Home, ".docker"),
            Path.Combine(Home, ".cargo", "bin"),
            Path.Combine(AppData, "Code", "User"),
            Path.Combine(LocalAppData, "JetBrains", "Toolbox"),
        ];

        foreach (JunkSource source in sources)
        {
            Assert.DoesNotContain(
                forbidden,
                f => JunkRootPlanner.IsSameOrUnder(JunkRootPlanner.Normalize(source.Path), JunkRootPlanner.Normalize(f), StringComparison.OrdinalIgnoreCase)
                    || JunkRootPlanner.IsSameOrUnder(JunkRootPlanner.Normalize(f), JunkRootPlanner.Normalize(source.Path), StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void Windows_JetBrains_ListsOnlyCachesAndLogsOfEachProduct_NeverToolbox()
    {
        using var dir = new TestDirectory();
        string local = Path.Combine(dir.Path, "Local");
        Directory.CreateDirectory(Path.Combine(local, "JetBrains", "Rider2025.2"));
        Directory.CreateDirectory(Path.Combine(local, "JetBrains", "Toolbox"));

        var jetBrains = DeveloperCacheSources.ForWindows(local, AppData, Home)
            .Where(s => s.Description.StartsWith("JetBrains", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, jetBrains.Count);
        Assert.DoesNotContain(jetBrains, s => s.Path.Contains("Toolbox", StringComparison.OrdinalIgnoreCase));
        Assert.All(jetBrains, s => Assert.True(
            Path.GetFileName(s.Path) is "caches" or "log", s.Path));
        Assert.False(jetBrains.Single(s => s.Description == "JetBrains Caches (Rider2025.2)").SelectedByDefault);
        Assert.True(jetBrains.Single(s => s.Description == "JetBrains Logs (Rider2025.2)").SelectedByDefault);
    }
}
