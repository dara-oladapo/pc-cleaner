using PcCleaner.Core.Models;
using PcCleaner.Core.Platform.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class LinuxJunkPlannerTests
{
    private const string Home = "/home/ada";
    private static readonly LinuxMountTable RootOnly = new([new LinuxMount("/", "ext4")]);

    private static IReadOnlyList<LinuxJunkSource> Plan(
        FakeLinuxFileSystem fs,
        LinuxMountTable? mounts = null,
        Func<string, string?>? env = null)
    {
        env ??= _ => null;
        var xdg = XdgBaseDirectories.Resolve(env, Home);
        return LinuxJunkPlanner.Plan(Home, xdg, env, fs, mounts ?? RootOnly);
    }

    private static LinuxJunkSource Single(IReadOnlyList<LinuxJunkSource> plan, string path) =>
        Assert.Single(plan, s => s.Path == path);

    [Fact]
    public void Plan_CategorizesTheNamedRules()
    {
        var fs = new FakeLinuxFileSystem()
            .Directory(
                $"{Home}/.cache/google-chrome/Default/Cache",
                $"{Home}/.cache/chromium",
                $"{Home}/.cache/BraveSoftware",
                $"{Home}/.cache/pip",
                $"{Home}/.npm/_cacache",
                $"{Home}/.cache/yarn",
                $"{Home}/.cargo/registry/cache",
                $"{Home}/.cache/go-build",
                $"{Home}/.cache/thumbnails/normal",
                $"{Home}/.local/share/Trash/files",
                $"{Home}/.var/app/org.mozilla.firefox/cache");

        var plan = Plan(fs);

        Assert.Equal(JunkCategory.BrowserCache, Single(plan, $"{Home}/.cache/google-chrome").Category);
        Assert.Equal(JunkCategory.BrowserCache, Single(plan, $"{Home}/.cache/chromium").Category);
        Assert.Equal(JunkCategory.BrowserCache, Single(plan, $"{Home}/.cache/BraveSoftware").Category);
        Assert.Equal(JunkCategory.BrowserCache, Single(plan, $"{Home}/.var/app/org.mozilla.firefox/cache").Category);
        Assert.Equal(JunkCategory.PackageManagerCache, Single(plan, $"{Home}/.cache/pip").Category);
        Assert.Equal(JunkCategory.PackageManagerCache, Single(plan, $"{Home}/.npm/_cacache").Category);
        Assert.Equal(JunkCategory.PackageManagerCache, Single(plan, $"{Home}/.cache/yarn").Category);
        Assert.Equal(JunkCategory.PackageManagerCache, Single(plan, $"{Home}/.cargo/registry/cache").Category);
        Assert.Equal(JunkCategory.PackageManagerCache, Single(plan, $"{Home}/.cache/go-build").Category);
        Assert.Equal(JunkCategory.SystemCache, Single(plan, $"{Home}/.cache/thumbnails").Category);
        Assert.Equal(JunkCategory.TrashBin, Single(plan, $"{Home}/.local/share/Trash").Category);
        Assert.All(plan, s => Assert.True(s.IsDirectory));
    }

    [Fact]
    public void Plan_NeverIncludesTheNuGetGlobalPackagesFolder()
    {
        var fs = new FakeLinuxFileSystem().Directory($"{Home}/.nuget/packages/newtonsoft.json");

        Assert.DoesNotContain(Plan(fs), s => s.Path.Contains("/.nuget", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_SkipsMissingSources()
    {
        var plan = Plan(new FakeLinuxFileSystem().Directory(Home));

        Assert.Empty(plan);
    }

    [Fact]
    public void Plan_FollowsXdgAndToolSpecificOverrides()
    {
        var env = new Dictionary<string, string?>
        {
            ["XDG_CACHE_HOME"] = "/fast/ada-cache",
            ["XDG_DATA_HOME"] = "/data/ada",
            ["CARGO_HOME"] = "/opt/cargo",
            ["GOCACHE"] = "/scratch/gocache",
            ["npm_config_cache"] = "/scratch/npm",
        };
        var fs = new FakeLinuxFileSystem().Directory(
            "/fast/ada-cache/pip",
            "/data/ada/Trash",
            "/opt/cargo/registry/cache",
            "/scratch/gocache",
            "/scratch/npm/_cacache",
            $"{Home}/.cache/pip"); // the default location is ignored once XDG_CACHE_HOME is set

        var plan = Plan(fs, env: n => env.GetValueOrDefault(n));

        Assert.Equal(
            new[] { "/fast/ada-cache/pip", "/scratch/npm/_cacache", "/opt/cargo/registry/cache", "/scratch/gocache", "/data/ada/Trash" },
            plan.Select(s => s.Path));
    }

    [Fact]
    public void Plan_FindsFirefoxCache2PerProfile_AndLeavesTheRestOfTheProfileToSystemCache()
    {
        var fs = new FakeLinuxFileSystem()
            .Directory(
                $"{Home}/.cache/mozilla/firefox/abc.default/cache2/entries",
                $"{Home}/.cache/mozilla/firefox/abc.default/startupCache",
                $"{Home}/.cache/mozilla/firefox/xyz.work/cache2")
            .File($"{Home}/.cache/mozilla/firefox/profiles.ini");

        var plan = Plan(fs);

        Assert.Equal("Firefox Cache (abc.default)", Single(plan, $"{Home}/.cache/mozilla/firefox/abc.default/cache2").Description);
        Assert.Equal(JunkCategory.BrowserCache, Single(plan, $"{Home}/.cache/mozilla/firefox/xyz.work/cache2").Category);

        var startup = Single(plan, $"{Home}/.cache/mozilla/firefox/abc.default/startupCache");
        Assert.Equal(JunkCategory.SystemCache, startup.Category);
        Assert.Equal("Cache: mozilla/firefox/abc.default/startupCache", startup.Description);

        Assert.DoesNotContain(plan, s => s.Path.EndsWith("profiles.ini", StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_SystemCacheIsTheRestOfTheCache_WithNoOverlap()
    {
        var fs = new FakeLinuxFileSystem()
            .Directory(
                $"{Home}/.cache/google-chrome",
                $"{Home}/.cache/pip",
                $"{Home}/.cache/thumbnails",
                $"{Home}/.cache/fontconfig",
                $"{Home}/.cache/mesa_shader_cache")
            .File($"{Home}/.cache/motd.legal-displayed");

        var plan = Plan(fs);

        Assert.DoesNotContain(plan, s => s.Path == $"{Home}/.cache");
        Assert.Equal(JunkCategory.SystemCache, Single(plan, $"{Home}/.cache/fontconfig").Category);
        Assert.Equal(JunkCategory.SystemCache, Single(plan, $"{Home}/.cache/mesa_shader_cache").Category);
        Assert.DoesNotContain(plan, s => s.Path == $"{Home}/.cache/motd.legal-displayed");

        // Every source appears once, and no source sits inside another, so no byte is counted twice.
        foreach (LinuxJunkSource a in plan)
        {
            Assert.DoesNotContain(plan, b => !ReferenceEquals(a, b) && LinuxPath.IsSameOrUnder(b.Path, a.Path));
        }
    }

    [Fact]
    public void Plan_NeverOffersLooseFilesFromTheCache_OnlyDirectories()
    {
        // Seen on a real home: apps park device IDs and generated keys directly in ~/.cache.
        var fs = new FakeLinuxFileSystem()
            .Directory($"{Home}/.cache/helm/repository", $"{Home}/.cache/pip")
            .File(
                $"{Home}/.cache/deviceid",
                $"{Home}/.cache/clippy-local-jwt.pem",
                $"{Home}/.cache/helm/repository/index.yaml",
                $"{Home}/.cache/pip/selfcheck.json");

        var plan = Plan(fs);

        Assert.All(plan, s => Assert.True(s.IsDirectory));
        Assert.DoesNotContain(plan, s => s.Path.EndsWith("deviceid", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, s => s.Path.EndsWith(".pem", StringComparison.Ordinal));
        Assert.Equal(
            new[] { $"{Home}/.cache/helm", $"{Home}/.cache/pip" },
            plan.Select(s => s.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Plan_SkipsSymlinkedRoots_AndSymlinksInsideTheCache()
    {
        var fs = new FakeLinuxFileSystem()
            .Directory($"{Home}/.cache/fontconfig")
            .Symlink($"{Home}/.cache/pip", $"{Home}/.cache/elsewhere")
            .Other($"{Home}/.cache/ibus.sock");

        var plan = Plan(fs);

        Assert.Equal(new[] { $"{Home}/.cache/fontconfig" }, plan.Select(s => s.Path));
    }

    [Fact]
    public void Plan_RefusesEverythingBehindASymlinkedCacheHome()
    {
        // ~/.cache -> /mnt/shared: whatever is there is not ours to walk. lstat on ~/.cache/pip still says
        // "directory" (only the last component is not followed), so the check has to look at every parent.
        var fs = new FakeLinuxFileSystem()
            .Symlink($"{Home}/.cache")
            .Directory($"{Home}/.cache/pip", $"{Home}/.cache/thumbnails", "/mnt/shared/pip");

        Assert.Empty(Plan(fs));
    }

    [Fact]
    public void Plan_ToleratesASymlinkedHomeOrTopLevelDirectory()
    {
        // Fedora Silverblue: /home -> /var/home. The planner sees the path it was given, so this is about the
        // ancestor check stopping at the home directory rather than refusing every source.
        var fs = new FakeLinuxFileSystem().Symlink("/home").Directory($"{Home}/.cache/pip");

        Assert.Contains(Plan(fs), s => s.Path == $"{Home}/.cache/pip");
    }

    [Fact]
    public void Plan_NeverCrossesAMountPoint()
    {
        var fs = new FakeLinuxFileSystem().Directory(
            $"{Home}/.cache/google-chrome/Default",
            $"{Home}/.cache/vm-images/disk", // something mounted inside the cache
            $"{Home}/.cache/vm-images/notes",
            $"{Home}/.cache/fontconfig");
        var mounts = new LinuxMountTable(
        [
            new LinuxMount("/", "ext4"),
            new LinuxMount($"{Home}/.cache/google-chrome/Default", "ext4"),
            new LinuxMount($"{Home}/.cache/vm-images/disk", "xfs"),
        ]);

        var plan = Plan(fs, mounts);

        // A named root with a mount inside is dropped outright (its cleaner would walk onto the other device)...
        Assert.DoesNotContain(plan, s => s.Path.StartsWith($"{Home}/.cache/google-chrome", StringComparison.Ordinal));

        // ...and the catch-all opens up the directory holding a mount and takes only the siblings.
        Assert.Contains(plan, s => s.Path == $"{Home}/.cache/vm-images/notes");
        Assert.DoesNotContain(plan, s => s.Path.StartsWith($"{Home}/.cache/vm-images/disk", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, s => s.Path == $"{Home}/.cache/vm-images");
        Assert.Contains(plan, s => s.Path == $"{Home}/.cache/fontconfig");
    }

    [Fact]
    public void Plan_RefusesPseudoFileSystems()
    {
        var fs = new FakeLinuxFileSystem().Directory("/proc/cache/pip", "/sys/cache/fontconfig");

        Assert.Empty(Plan(fs, env: n => n == "XDG_CACHE_HOME" ? "/proc/cache" : null));
        Assert.Empty(Plan(fs, env: n => n == "XDG_CACHE_HOME" ? "/sys/cache" : null));
    }

    [Theory]
    [InlineData(Home)]
    [InlineData("/home")]
    [InlineData("/")]
    [InlineData("/tmp")]
    public void Plan_HasNoCatchAll_WhenCacheHomeWouldSweepRealData(string cacheHome)
    {
        var fs = new FakeLinuxFileSystem()
            .Directory($"{Home}/Documents/thesis", "/tmp/other-user", $"{cacheHome}/pip")
            .File($"{Home}/notes.txt");

        var plan = Plan(fs, env: n => n == "XDG_CACHE_HOME" ? cacheHome : null);

        Assert.All(plan, s => Assert.NotEqual(JunkCategory.SystemCache, s.Category));
        Assert.Contains(plan, s => s.Path == LinuxPath.Join(cacheHome, "pip"));
    }

    [Fact]
    public void Plan_TakesOnlyLogsFromStateHome()
    {
        var fs = new FakeLinuxFileSystem()
            .Directory(
                $"{Home}/.local/state/logs",
                $"{Home}/.local/state/someapp/log",
                $"{Home}/.local/state/someapp/sessions",
                $"{Home}/.local/state/deep/a/logs")
            .File(
                $"{Home}/.local/state/lesshst",
                $"{Home}/.local/state/wireplumber/stream-properties",
                $"{Home}/.local/state/editor/editor.log",
                $"{Home}/.local/state/.log");

        var plan = Plan(fs);

        Assert.Equal(
            new[] { $"{Home}/.local/state/editor/editor.log", $"{Home}/.local/state/logs", $"{Home}/.local/state/someapp/log" },
            plan.Select(s => s.Path).Order(StringComparer.Ordinal));
        Assert.All(plan, s => Assert.Equal(JunkCategory.SystemLogs, s.Category));
        Assert.False(Single(plan, $"{Home}/.local/state/editor/editor.log").IsDirectory);
        Assert.Equal("Logs: someapp/log", Single(plan, $"{Home}/.local/state/someapp/log").Description);
    }

    [Fact]
    public void Plan_DropsNestedDuplicates_WhenOverridesPointIntoEachOther()
    {
        // GOCACHE pointing inside pip's cache is silly, but must not produce two rows over the same bytes.
        var fs = new FakeLinuxFileSystem().Directory($"{Home}/.cache/pip/go");

        var plan = Plan(fs, env: n => n == "GOCACHE" ? $"{Home}/.cache/pip/go" : null);

        Assert.Equal(new[] { $"{Home}/.cache/pip" }, plan.Select(s => s.Path));
    }
}
