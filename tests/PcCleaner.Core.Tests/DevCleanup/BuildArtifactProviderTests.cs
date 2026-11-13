using PcCleaner.Core.DevCleanup;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests.DevCleanup;

public class BuildArtifactProviderTests
{
    /// <summary>40 days ahead, so files the test just wrote count as untouched for 40 days (threshold 30).</summary>
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(40);

    private static BuildArtifactProvider Provider(TestDirectory dir, DateTime? now = null)
    {
        var time = new FixedTime(now ?? Future);
        return new BuildArtifactProvider(new SystemProcessRunner(), new JunkCleaner(time), new DevCleanupOptions { Roots = [dir.Path] }, time);
    }

    /// <summary>A pushed repo holding a .NET project with source, plus bin/ and obj/ output.</summary>
    private static string CreateDotNetProject(TestDirectory dir, string gitignore = "bin/\nobj/\n")
    {
        string repo = TestGit.CreatePushedRepo(dir, gitignore);
        dir.WriteFile("repo/App/App.csproj", "<Project />");
        dir.WriteFile("repo/App/Program.cs", "class P {}");
        TestGit.Run(repo, "add", "-A");
        TestGit.Run(repo, "commit", "-m", "project");
        dir.WriteFileOfSize("repo/App/bin/Debug/App.dll", 1000);
        dir.WriteFileOfSize("repo/App/obj/project.assets.json", 500);
        return repo;
    }

    private static async Task<IReadOnlyList<DevCleanupItem>> Offered(BuildArtifactProvider provider) =>
        (await provider.MeasureAsync()).Items;

    [Fact]
    public async Task IgnoredOutputOfIdleProject_IsOffered()
    {
        using var dir = new TestDirectory();
        CreateDotNetProject(dir);

        var items = await Offered(Provider(dir));

        Assert.Equal(["bin", "obj"], items.Select(i => Path.GetFileName(i.Path!)).Order().ToArray());
        DevCleanupItem bin = items.Single(i => i.Path!.EndsWith("bin", StringComparison.Ordinal));
        Assert.Equal(1000, bin.SizeBytes);
        Assert.EndsWith("git-ignored", bin.Reason, StringComparison.Ordinal);
        Assert.Contains("idle", bin.Reason);
        Assert.False(bin.SelectedByDefault);
    }

    [Fact]
    public async Task NotIgnoredFolder_IsKept()
    {
        using var dir = new TestDirectory();
        CreateDotNetProject(dir, gitignore: "bin/\n");

        DevCleanupReport report = await Provider(dir).MeasureAsync();
        var items = report.Items;

        Assert.Contains("1 not git-ignored", report.Notes[0], StringComparison.Ordinal);
        Assert.DoesNotContain(items, i => Path.GetFileName(i.Path!) == "obj");
        Assert.Contains(items, i => Path.GetFileName(i.Path!) == "bin");
    }

    [Fact]
    public async Task RecentlyEditedProject_IsKept()
    {
        using var dir = new TestDirectory();
        CreateDotNetProject(dir);

        DevCleanupReport report = await Provider(dir, now: DateTime.UtcNow).MeasureAsync();
        Assert.Empty(report.Items);
        Assert.Contains("2 recently edited", report.Notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProjectOutsideAGitRepo_IsSkipped()
    {
        using var dir = new TestDirectory();
        dir.WriteFile("loose/Loose.csproj", "<Project />");
        dir.WriteFileOfSize("loose/bin/Loose.dll", 100);

        DevCleanupReport report = await Provider(dir).MeasureAsync();
        Assert.Empty(report.Items);
        Assert.Contains("1 not in a git repo", report.Notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task IgnoredFolderWithTrackedFiles_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = CreateDotNetProject(dir);
        TestGit.Run(repo, "add", "-f", "App/bin/Debug/App.dll");
        TestGit.Run(repo, "commit", "-m", "vendored binary");

        DevCleanupReport report = await Provider(dir).MeasureAsync();

        // git itself may already call a folder with tracked content "not ignored"; either way it must be kept.
        Assert.Matches("Kept: 1 (has tracked files|not git-ignored)", report.Notes[0]);
        Assert.DoesNotContain(report.Items, i => Path.GetFileName(i.Path!) == "bin");
    }

    [Fact]
    public async Task LinkedArtifactFolder_IsKept_AndItsTargetUntouched()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir, "node_modules/\n");
        dir.WriteFile("repo/web/package.json", "{}");
        TestGit.Run(repo, "add", "-A");
        TestGit.Run(repo, "commit", "-m", "web");
        string target = Path.Combine(dir.Path, "elsewhere");
        string precious = dir.WriteFileOfSize("elsewhere/keep.bin", 100);
        if (!TestLinks.TryCreateDirectoryLink(Path.Combine(repo, "web", "node_modules"), target))
        {
            return;
        }

        Assert.Empty(await Offered(Provider(dir)));
        Assert.True(File.Exists(precious));
    }

    [Fact]
    public async Task Clean_EmptiesTheFolderThroughTheJunkPipeline_AndLeavesSourceAlone()
    {
        using var dir = new TestDirectory();
        string repo = CreateDotNetProject(dir);
        BuildArtifactProvider provider = Provider(dir);
        DevCleanupItem bin = (await Offered(provider)).Single(i => Path.GetFileName(i.Path!) == "bin");

        DevCleanupResult result = await provider.CleanAsync([bin]);

        Assert.Single(result.Succeeded);
        Assert.Equal(1000, result.BytesFreed);
        string binDir = Path.Combine(repo, "App", "bin");
        Assert.True(Directory.Exists(binDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(binDir));
        Assert.True(File.Exists(Path.Combine(repo, "App", "Program.cs")));
        Assert.True(File.Exists(Path.Combine(repo, "App", "obj", "project.assets.json")));
    }

    [Fact]
    public async Task Clean_RechecksFirst_AndKeepsAFolderNoLongerIgnored()
    {
        using var dir = new TestDirectory();
        string repo = CreateDotNetProject(dir);
        BuildArtifactProvider provider = Provider(dir);
        DevCleanupItem bin = (await Offered(provider)).Single(i => Path.GetFileName(i.Path!) == "bin");

        File.WriteAllText(Path.Combine(repo, ".gitignore"), "obj/\n");
        DevCleanupResult result = await provider.CleanAsync([bin]);

        Assert.Empty(result.Succeeded);
        Assert.Contains("not git-ignored", Assert.Single(result.Failed).Error);
        Assert.True(File.Exists(Path.Combine(repo, "App", "bin", "Debug", "App.dll")));
    }
}
