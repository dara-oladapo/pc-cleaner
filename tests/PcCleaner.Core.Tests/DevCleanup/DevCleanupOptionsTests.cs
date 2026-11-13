using PcCleaner.Core.DevCleanup;

namespace PcCleaner.Core.Tests.DevCleanup;

public class DevCleanupOptionsTests
{
    private static readonly string Home = Path.Combine(Path.GetTempPath(), "home-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void DefaultRoots_CoverCommonConventions_ThatExist()
    {
        var roots = DevCleanupOptions.DefaultRoots(Home, isMac: false, exists: _ => true);

        string[] expected =
        [
            Path.Combine(Home, "source", "repos"),
            Path.Combine(Home, "repos"),
            Path.Combine(Home, "src"),
            Path.Combine(Home, "code"),
            Path.Combine(Home, "Projects"),
            Path.Combine(Home, "Dev"),
            Path.Combine(Home, "Documents", "GitHub"),
        ];
        Assert.Equal(expected, roots);
    }

    [Fact]
    public void DefaultRoots_AddDeveloperOnMac_AndSkipMissingFolders()
    {
        string developer = Path.Combine(Home, "Developer");
        string src = Path.Combine(Home, "src");

        var roots = DevCleanupOptions.DefaultRoots(Home, isMac: true, exists: p => p == developer || p == src);

        Assert.Equal([src, developer], roots);
    }

    [Fact]
    public void DefaultRoots_NeverLeaveTheHomeFolder()
    {
        var roots = DevCleanupOptions.DefaultRoots(Home, isMac: true, exists: _ => true);

        Assert.All(roots, r => Assert.StartsWith(Home, r, StringComparison.Ordinal));
    }
}
