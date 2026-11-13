using PcCleaner.Core.DevCleanup;

namespace PcCleaner.Core.Tests.DevCleanup;

/// <summary>Real git, in a temp directory the test owns. Nothing outside it is read or changed.</summary>
public class GitWorktreeProviderTests
{
    /// <summary>A clock 30 days ahead, so worktrees the test just made count as idle for 30 days.</summary>
    private static readonly DateTime Future = DateTime.UtcNow.AddDays(30);

    private static GitWorktreeProvider Provider(TestDirectory dir, DateTime? now = null) =>
        new(new SystemProcessRunner(), new DevCleanupOptions { Roots = [dir.Path] }, new FixedTime(now ?? Future));

    private static string AddWorktree(string repo, TestDirectory dir, string name)
    {
        string path = Path.Combine(dir.Path, name);
        TestGit.Run(repo, "worktree", "add", path, "-b", name);
        return path;
    }

    private static async Task<IReadOnlyList<DevCleanupItem>> OfferedWorktrees(GitWorktreeProvider provider)
    {
        DevCleanupReport report = await provider.MeasureAsync();
        Assert.Empty(report.Errors);
        return report.Items;
    }

    /// <summary>Nothing offered, and the report says it was kept for the expected reason (not for some other one).</summary>
    private static async Task AssertKept(GitWorktreeProvider provider, string reason)
    {
        DevCleanupReport report = await provider.MeasureAsync();
        Assert.Empty(report.Errors);
        Assert.Empty(report.Items);
        Assert.Contains($"Kept: 1 {reason}", report.Notes[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task CleanPushedIdleWorktree_IsOffered_WithReason()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-stale");

        var items = await OfferedWorktrees(Provider(dir));

        DevCleanupItem item = Assert.Single(items);
        // By name: on macOS git reports the /private/var realpath of the temp folder.
        Assert.Equal(Path.GetFileName(wt), Path.GetFileName(item.Path));
        Assert.StartsWith("worktree clean, pushed, idle ", item.Reason, StringComparison.Ordinal);
        Assert.Equal(DevCleanupRisk.Low, item.Risk);
        Assert.False(item.SelectedByDefault);
    }

    [Fact]
    public async Task MainWorktree_IsNeverOffered()
    {
        using var dir = new TestDirectory();
        TestGit.CreatePushedRepo(dir);

        Assert.Empty(await OfferedWorktrees(Provider(dir)));
    }

    [Fact]
    public async Task ModifiedTrackedFile_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-dirty");
        File.WriteAllText(Path.Combine(wt, "README.md"), "changed");

        await AssertKept(Provider(dir), "uncommitted or untracked changes");
    }

    [Fact]
    public async Task UntrackedFile_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-untracked");
        Directory.CreateDirectory(Path.Combine(wt, "notes"));
        File.WriteAllText(Path.Combine(wt, "notes", "todo.txt"), "draft");

        await AssertKept(Provider(dir), "uncommitted or untracked changes");
    }

    [Fact]
    public async Task UnpushedCommit_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-unpushed");
        File.WriteAllText(Path.Combine(wt, "feature.txt"), "work");
        TestGit.Run(wt, "add", "-A");
        TestGit.Run(wt, "commit", "-m", "local only");

        await AssertKept(Provider(dir), "unpushed commits");
    }

    [Fact]
    public async Task LockedWorktree_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-locked");
        TestGit.Run(repo, "worktree", "lock", wt);

        await AssertKept(Provider(dir), "locked");
    }

    [Fact]
    public async Task RecentlyActiveWorktree_IsKept()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        AddWorktree(repo, dir, "wt-recent");

        await AssertKept(Provider(dir, now: DateTime.UtcNow), "recently active");
    }

    [Fact]
    public async Task IgnoredNonRegenerableFiles_RaiseRiskAndWarn()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir, gitignore: ".env\nbin/\n");
        string wt = AddWorktree(repo, dir, "wt-env");
        File.WriteAllText(Path.Combine(wt, ".env"), "SECRET=1");
        // Nested, as real projects are: git reports it as src/App/bin/Debug/, which is still build output.
        Directory.CreateDirectory(Path.Combine(wt, "src", "App", "bin", "Debug"));
        File.WriteAllText(Path.Combine(wt, "src", "App", "bin", "Debug", "a.dll"), "x");

        DevCleanupItem item = Assert.Single(await OfferedWorktrees(Provider(dir)));

        Assert.Equal(DevCleanupRisk.High, item.Risk);
        Assert.Contains(".env", item.Warning);
        Assert.DoesNotContain("bin", item.Warning);
    }

    [Fact]
    public async Task IgnoredBuildOutputOnly_StaysLowRisk()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir, gitignore: "bin/\nobj/\n");
        string wt = AddWorktree(repo, dir, "wt-built");
        Directory.CreateDirectory(Path.Combine(wt, "src", "App", "bin", "Debug"));
        File.WriteAllText(Path.Combine(wt, "src", "App", "bin", "Debug", "a.dll"), "x");

        DevCleanupItem item = Assert.Single(await OfferedWorktrees(Provider(dir)));

        Assert.Equal(DevCleanupRisk.Low, item.Risk);
        Assert.Null(item.Warning);
    }

    [Fact]
    public async Task MissingWorktreeFolder_IsOfferedAsPrune_AndPruneDropsTheRecord()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-gone");
        Directory.Delete(wt, recursive: true);
        GitWorktreeProvider provider = Provider(dir);

        DevCleanupItem item = Assert.Single(await OfferedWorktrees(provider));
        Assert.StartsWith("Prune 1 missing", item.Title, StringComparison.Ordinal);

        DevCleanupResult result = await provider.CleanAsync([item]);

        Assert.Single(result.Succeeded);
        Assert.DoesNotContain("wt-gone", TestGit.Run(repo, "worktree", "list", "--porcelain"));
    }

    [Fact]
    public async Task Clean_RemovesTheWorktree_ButKeepsTheBranch()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-done");
        GitWorktreeProvider provider = Provider(dir);
        DevCleanupItem item = Assert.Single(await OfferedWorktrees(provider));

        DevCleanupResult result = await provider.CleanAsync([item]);

        Assert.Single(result.Succeeded);
        Assert.Empty(result.Failed);
        Assert.False(Directory.Exists(wt));
        Assert.Contains("wt-done", TestGit.Run(repo, "branch", "--list", "wt-done"));
    }

    [Fact]
    public async Task Clean_RechecksFirst_AndLeavesAWorktreeThatGainedWork()
    {
        using var dir = new TestDirectory();
        string repo = TestGit.CreatePushedRepo(dir);
        string wt = AddWorktree(repo, dir, "wt-revived");
        GitWorktreeProvider provider = Provider(dir);
        DevCleanupItem item = Assert.Single(await OfferedWorktrees(provider));

        File.WriteAllText(Path.Combine(wt, "new-work.txt"), "started again");
        DevCleanupResult result = await provider.CleanAsync([item]);

        Assert.Empty(result.Succeeded);
        Assert.Contains("no longer stale", Assert.Single(result.Failed).Error);
        Assert.True(File.Exists(Path.Combine(wt, "new-work.txt")));
    }

    [Theory]
    [InlineData(true, false, true, 0, 0, 30, false, "main worktree")]
    [InlineData(false, true, true, 0, 0, 30, false, "locked")]
    [InlineData(false, false, true, 1, 0, 30, false, "uncommitted or untracked changes")]
    [InlineData(false, false, true, 0, 2, 30, false, "unpushed commits")]
    [InlineData(false, false, true, 0, 0, 3, false, "recently active")]
    [InlineData(false, false, true, 0, 0, 30, true, "worktree clean, pushed, idle 30 days")]
    public void Decide_AppliesEveryRule(bool isMain, bool isLocked, bool exists, int changed, int unpushed, int idleDays, bool stale, string reasonStart)
    {
        DateTime now = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);
        var facts = new WorktreeFacts(
            isMain, isLocked, exists, null, Enumerable.Repeat(" M file", changed).ToList(), [], null, unpushed, now.AddDays(-idleDays));

        WorktreeVerdict verdict = GitWorktreeProvider.Decide(facts, now, idleDays: 14);

        Assert.Equal(stale, verdict.IsStale);
        Assert.StartsWith(reasonStart, verdict.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Decide_KeepsWhenGitCouldNotAnswer()
    {
        DateTime now = DateTime.UtcNow;
        var statusFailed = new WorktreeFacts(false, false, true, "fatal: detected dubious ownership", [], [], null, 0, now.AddDays(-60));
        var revListFailed = new WorktreeFacts(false, false, true, null, [], [], "fatal: bad revision", 0, now.AddDays(-60));
        var unknownAge = new WorktreeFacts(false, false, true, null, [], [], null, 0, null);

        Assert.False(GitWorktreeProvider.Decide(statusFailed, now, 14).IsStale);
        Assert.False(GitWorktreeProvider.Decide(revListFailed, now, 14).IsStale);
        Assert.False(GitWorktreeProvider.Decide(unknownAge, now, 14).IsStale);
    }

    [Fact]
    public void ParsePorcelain_ReadsMainLockedPrunableAndDetached()
    {
        string output = string.Join('\n',
            "worktree C:/Dev/repo", "HEAD aaa", "branch refs/heads/main", "",
            "worktree C:/Dev/repo/.claude/worktrees/a", "HEAD bbb", "detached", "locked agent running", "",
            "worktree C:/Dev/gone", "HEAD ccc", "branch refs/heads/feat/x", "prunable gitdir file points to non-existent location", "");

        var entries = GitWorktreeProvider.ParsePorcelain(output);

        Assert.Equal(3, entries.Count);
        Assert.True(entries[0].IsMain);
        Assert.Equal("main", entries[0].Branch);
        Assert.True(entries[1].IsLocked);
        Assert.Null(entries[1].Branch);
        Assert.True(entries[2].IsPrunable);
        Assert.Equal("feat/x", entries[2].Branch);
    }
}
