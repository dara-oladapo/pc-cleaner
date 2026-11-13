using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

/// <summary>Pure path logic: nothing here touches the file system.</summary>
public class JunkRootPlannerTests
{
    // An absolute base that is valid on every OS; never created or read.
    private static readonly string Base = Path.Combine(Path.GetTempPath(), "planner-" + Guid.NewGuid().ToString("N"));

    private static string P(params string[] parts) => Path.Combine([Base, .. parts]);

    [Fact]
    public void IsSameOrUnder_RespectsSeparatorBoundary()
    {
        string temp = JunkRootPlanner.Normalize(P("Temp"));
        Assert.True(JunkRootPlanner.IsSameOrUnder(JunkRootPlanner.Normalize(P("Temp", "a")), temp, StringComparison.Ordinal));
        Assert.True(JunkRootPlanner.IsSameOrUnder(temp, temp, StringComparison.Ordinal));
        Assert.False(JunkRootPlanner.IsSameOrUnder(JunkRootPlanner.Normalize(P("TempOther")), temp, StringComparison.Ordinal));
        Assert.False(JunkRootPlanner.IsStrictlyUnder(temp, temp, StringComparison.Ordinal));
    }

    [Fact]
    public void Normalize_TrimsTrailingSeparator()
    {
        Assert.Equal(
            JunkRootPlanner.Normalize(P("Temp")),
            JunkRootPlanner.Normalize(P("Temp") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Plan_ExactDuplicate_KeptOnce()
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Temp") + Path.DirectorySeparatorChar, JunkCategory.TempFiles, "User Temp", IsTemp: true),
                new JunkSource(P("Temp"), JunkCategory.TempFiles, "Local Temp", IsTemp: true),
            ],
            StringComparison.Ordinal);

        Assert.Single(plan);
        Assert.Equal("User Temp", plan[0].Description);
    }

    [Fact]
    public void Plan_CaseInsensitiveDuplicate_KeptOnceWhenComparisonIgnoresCase()
    {
        JunkSource[] sources =
        [
            new(P("Temp"), JunkCategory.TempFiles, "A", IsTemp: true),
            new(P("TEMP"), JunkCategory.TempFiles, "B", IsTemp: true),
        ];

        Assert.Single(JunkRootPlanner.Plan(sources, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(2, JunkRootPlanner.Plan(sources, StringComparison.Ordinal).Count);
    }

    [Fact]
    public void Plan_NestedRoot_ParentExcludesChild_ChildKeepsLabelAndInheritsTempRules()
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Temp"), JunkCategory.TempFiles, "Temp", IsTemp: true),
                new JunkSource(P("temp", "NuGetScratch"), JunkCategory.PackageManagerCache, "NuGet Scratch"),
            ],
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(2, plan.Count);
        PlannedJunkRoot parent = plan.Single(r => r.Description == "Temp");
        PlannedJunkRoot child = plan.Single(r => r.Description == "NuGet Scratch");

        Assert.Equal(JunkCategory.PackageManagerCache, child.Category);
        Assert.True(child.IsTemp);
        Assert.Single(parent.ExcludedPaths);
        Assert.Equal(child.Path, parent.ExcludedPaths[0], ignoreCase: true);
        Assert.Empty(child.ExcludedPaths);
    }

    [Fact]
    public void Plan_MacStyleCachesTree_ExcludesEveryNestedCache()
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Library", "Caches"), JunkCategory.SystemCache, "User Caches"),
                new JunkSource(P("Library", "Caches", "com.apple.Safari"), JunkCategory.BrowserCache, "Safari"),
                new JunkSource(P("Library", "Caches", "Homebrew"), JunkCategory.PackageManagerCache, "Homebrew"),
                new JunkSource(P("Library", "Caches", "pip"), JunkCategory.PackageManagerCache, "pip"),
                new JunkSource(P("Library", "CachesOther"), JunkCategory.SystemCache, "Sibling"),
            ],
            StringComparison.Ordinal);

        PlannedJunkRoot caches = plan.Single(r => r.Description == "User Caches");
        Assert.Equal(3, caches.ExcludedPaths.Count);
        Assert.False(caches.IsTemp);
        Assert.Empty(plan.Single(r => r.Description == "Sibling").ExcludedPaths);
    }

    [Fact]
    public void Plan_SelectedByDefault_DefaultsToTrueAndCarriesThrough()
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Cheap"), JunkCategory.DeveloperCache, "Cheap"),
                new JunkSource(P("Store"), JunkCategory.DeveloperCache, "Store", SelectedByDefault: false),
            ],
            StringComparison.Ordinal);

        Assert.True(plan.Single(r => r.Description == "Cheap").SelectedByDefault);
        Assert.False(plan.Single(r => r.Description == "Store").SelectedByDefault);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Plan_ExactDuplicate_EitherCopyOptIn_SurvivorIsOptIn(bool first, bool second)
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Store"), JunkCategory.DeveloperCache, "A", SelectedByDefault: first),
                new JunkSource(P("Store"), JunkCategory.DeveloperCache, "B", SelectedByDefault: second),
            ],
            StringComparison.Ordinal);

        Assert.False(Assert.Single(plan).SelectedByDefault);
    }

    [Fact]
    public void Plan_NestedRoot_OptInIsInheritedByChildButNotByParent()
    {
        var plan = JunkRootPlanner.Plan(
            [
                new JunkSource(P("Caches"), JunkCategory.SystemCache, "User Caches"),
                new JunkSource(P("Caches", "ms-playwright"), JunkCategory.DeveloperCache, "Playwright", SelectedByDefault: false),
                new JunkSource(P("Caches", "ms-playwright", "chromium"), JunkCategory.DeveloperCache, "Chromium"),
            ],
            StringComparison.Ordinal);

        Assert.True(plan.Single(r => r.Description == "User Caches").SelectedByDefault);
        Assert.False(plan.Single(r => r.Description == "Playwright").SelectedByDefault);
        Assert.False(plan.Single(r => r.Description == "Chromium").SelectedByDefault);
    }
}
