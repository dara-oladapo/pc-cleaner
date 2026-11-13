using System.Globalization;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.DevCleanup;

/// <summary>One entry of <c>git worktree list --porcelain</c>.</summary>
public sealed record GitWorktreeEntry(string Path, string? Head, string? Branch, bool IsMain, bool IsBare, bool IsLocked, bool IsPrunable);

/// <summary>Everything the staleness rules look at for one worktree, gathered fresh from git and the file system.</summary>
public sealed record WorktreeFacts(
    bool IsMain,
    bool IsLocked,
    bool Exists,
    string? StatusError,
    IReadOnlyList<string> ChangedEntries,
    IReadOnlyList<string> IgnoredEntries,
    string? UnpushedError,
    int UnpushedCommits,
    DateTime? LastActivityUtc);

/// <summary>Whether a worktree is offered, and the one-line reason either way.</summary>
public sealed record WorktreeVerdict(bool IsStale, string Reason, DevCleanupRisk Risk = DevCleanupRisk.Low, string? Warning = null);

/// <summary>
/// Offers linked git worktrees that are provably finished with, and removes them only through
/// <c>git worktree remove</c> (never <c>--force</c>, never a raw delete, never touching branches).
/// </summary>
public sealed class GitWorktreeProvider : IDevCleanupProvider
{
    public const string ProviderId = "git-worktrees";

    /// <summary>Ignored entries that are plainly regenerable; anything else ignored in a worktree raises its risk.</summary>
    private static readonly HashSet<string> RegenerableIgnoredNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".next", ".turbo", ".nuxt", "target", "build", ".gradle", ".venv",
        "__pycache__", "dist", "out", ".vs", "coverage", ".pytest_cache", ".mypy_cache", "TestResults",
    };

    private readonly IProcessRunner _runner;
    private readonly DevCleanupOptions _options;
    private readonly TimeProvider _time;

    public GitWorktreeProvider(IProcessRunner runner, DevCleanupOptions options)
        : this(runner, options, TimeProvider.System)
    {
    }

    public GitWorktreeProvider(IProcessRunner runner, DevCleanupOptions options, TimeProvider time)
    {
        _runner = runner;
        _options = options;
        _time = time;
    }

    public string Id => ProviderId;

    public string DisplayName => "Git worktrees";

    public Task<bool> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ToolLocator.IsInstalled("git") && _options.Roots.Any(Directory.Exists), cancellationToken);

    public async Task<DevCleanupReport> MeasureAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> repos = await Task.Run(
            () => DevTreeWalker.FindRepositories(_options.Roots, _options.MaxDepth, cancellationToken), cancellationToken).ConfigureAwait(false);

        var items = new List<DevCleanupItem>();
        var errors = new List<string>();
        var kept = new Dictionary<string, int>(StringComparer.Ordinal);
        int checkedCount = 0;

        foreach (string repo in repos)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ProcessResult list = await Git(repo, cancellationToken, "worktree", "list", "--porcelain").ConfigureAwait(false);
            if (!list.Succeeded)
            {
                errors.Add($"{repo}: git worktree list failed: {list.ErrorText}");
                continue;
            }

            IReadOnlyList<GitWorktreeEntry> entries = ParsePorcelain(list.StandardOutput);
            GitWorktreeEntry? main = entries.FirstOrDefault(e => e.IsMain);
            if (main is null)
            {
                continue;
            }

            var missing = new List<GitWorktreeEntry>();
            foreach (GitWorktreeEntry entry in entries.Where(e => !e.IsMain))
            {
                checkedCount++;
                if (!entry.IsLocked && (entry.IsPrunable || !Directory.Exists(entry.Path)))
                {
                    missing.Add(entry);
                    continue;
                }

                WorktreeFacts facts = await GatherFactsAsync(entry, cancellationToken).ConfigureAwait(false);
                WorktreeVerdict verdict = Decide(facts, _time.GetUtcNow().UtcDateTime, _options.WorktreeIdleDays);
                if (!verdict.IsStale)
                {
                    string bucket = verdict.Reason.Split(';', 2)[0];
                    kept[bucket] = kept.GetValueOrDefault(bucket) + 1;
                    continue;
                }

                long size = await Task.Run(() => DevTreeWalker.MeasureDirectory(entry.Path, cancellationToken), cancellationToken).ConfigureAwait(false);
                string branch = entry.Branch is null ? "detached HEAD" : entry.Branch;
                items.Add(new DevCleanupItem(Id, $"Worktrees of {main.Path}", $"{System.IO.Path.GetFileName(entry.Path)} ({branch})", verdict.Reason, size, verdict.Risk)
                {
                    Path = entry.Path,
                    Warning = verdict.Warning,
                    Payload = new RemoveAction(main.Path, entry),
                });
            }

            if (missing.Count > 0)
            {
                items.Add(new DevCleanupItem(
                    Id,
                    $"Worktrees of {main.Path}",
                    $"Prune {missing.Count} missing worktree record(s)",
                    "Their folders no longer exist; 'git worktree prune' drops the leftover records. A worktree on an "
                        + "unplugged drive looks missing too; lock it ('git worktree lock') to keep its record.",
                    0,
                    DevCleanupRisk.Medium)
                {
                    Path = main.Path,
                    Payload = new PruneAction(main.Path),
                });
            }
        }

        var notes = new List<string>
        {
            $"Checked {checkedCount} linked worktree(s) in {repos.Count} repositories under {string.Join(", ", _options.Roots)}."
                + (kept.Count == 0 ? string.Empty : " Kept: " + string.Join(", ", kept.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")) + "."),
            "Removing a worktree never deletes its branch.",
        };

        return new DevCleanupReport(Id, DisplayName, items, notes, errors);
    }

    public async Task<DevCleanupResult> CleanAsync(IReadOnlyList<DevCleanupItem> items, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<DevCleanupItem>();
        var failed = new List<(DevCleanupItem, string)>();
        var messages = new List<string>();
        long freed = 0;

        foreach (DevCleanupItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (item.Payload)
            {
                case RemoveAction remove:
                {
                    // Every rule is re-checked right now: the worktree may have been used since the scan.
                    WorktreeFacts facts = await GatherFactsAsync(remove.Entry, cancellationToken).ConfigureAwait(false);
                    WorktreeVerdict verdict = Decide(facts, _time.GetUtcNow().UtcDateTime, _options.WorktreeIdleDays);
                    if (!verdict.IsStale)
                    {
                        failed.Add((item, $"{remove.Entry.Path}: no longer stale ({verdict.Reason}); left alone."));
                        continue;
                    }

                    ProcessResult result = await Git(remove.MainPath, cancellationToken, "worktree", "remove", remove.Entry.Path).ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        succeeded.Add(item);
                        freed += Directory.Exists(remove.Entry.Path) ? 0 : item.SizeBytes ?? 0;
                    }
                    else
                    {
                        failed.Add((item, $"{remove.Entry.Path}: {result.ErrorText}"));
                    }

                    break;
                }

                case PruneAction prune:
                {
                    ProcessResult result = await Git(prune.MainPath, cancellationToken, "worktree", "prune", "--verbose").ConfigureAwait(false);
                    if (result.Succeeded)
                    {
                        succeeded.Add(item);
                        string said = (result.StandardOutput + result.StandardError).Trim();
                        if (said.Length > 0)
                        {
                            messages.Add($"{prune.MainPath}: {said}");
                        }
                    }
                    else
                    {
                        failed.Add((item, $"{prune.MainPath}: {result.ErrorText}"));
                    }

                    break;
                }

                default:
                    failed.Add((item, "Not a git worktree item."));
                    break;
            }
        }

        return new DevCleanupResult(succeeded, failed, freed) { Messages = messages };
    }

    /// <summary>
    /// The staleness rules. Offered only if ALL hold: not the main worktree, not locked, folder present,
    /// <c>git status</c> clean including untracked files, nothing unpushed, and idle for <paramref name="idleDays"/>.
    /// Anything git could not answer counts as "keep".
    /// </summary>
    public static WorktreeVerdict Decide(WorktreeFacts facts, DateTime nowUtc, int idleDays)
    {
        if (facts.IsMain)
        {
            return new WorktreeVerdict(false, "main worktree");
        }

        if (facts.IsLocked)
        {
            return new WorktreeVerdict(false, "locked");
        }

        if (!facts.Exists)
        {
            return new WorktreeVerdict(false, "folder missing");
        }

        if (facts.StatusError is not null)
        {
            return new WorktreeVerdict(false, $"git status failed; {facts.StatusError}");
        }

        if (facts.ChangedEntries.Count > 0)
        {
            return new WorktreeVerdict(false, $"uncommitted or untracked changes; {facts.ChangedEntries.Count} entries");
        }

        if (facts.UnpushedError is not null)
        {
            return new WorktreeVerdict(false, $"could not check for unpushed commits; {facts.UnpushedError}");
        }

        if (facts.UnpushedCommits > 0)
        {
            return new WorktreeVerdict(false, $"unpushed commits; {facts.UnpushedCommits}");
        }

        if (facts.LastActivityUtc is not { } last)
        {
            return new WorktreeVerdict(false, "last activity unknown");
        }

        int idle = (int)Math.Floor((nowUtc - last).TotalDays);
        if (nowUtc - last < TimeSpan.FromDays(idleDays))
        {
            return new WorktreeVerdict(false, $"recently active; {Math.Max(idle, 0)} days ago");
        }

        string reason = $"worktree clean, pushed, idle {idle} days";
        var unusual = facts.IgnoredEntries
            .Where(e => !RegenerableIgnoredNames.Contains(LastSegment(e)))
            .ToList();
        if (unusual.Count == 0)
        {
            return new WorktreeVerdict(true, reason);
        }

        string sample = string.Join(", ", unusual.Take(3)) + (unusual.Count > 3 ? $" and {unusual.Count - 3} more" : string.Empty);
        return new WorktreeVerdict(
            true,
            reason + $"; holds git-ignored files ({sample})",
            DevCleanupRisk.High,
            $"git worktree remove also deletes ignored files, and this worktree has some that may not be regenerable: {sample}.");
    }

    public static IReadOnlyList<GitWorktreeEntry> ParsePorcelain(string output)
    {
        var entries = new List<GitWorktreeEntry>();
        string? path = null, head = null, branch = null;
        bool bare = false, locked = false, prunable = false;

        void Flush()
        {
            if (path is not null)
            {
                entries.Add(new GitWorktreeEntry(NormalizeGitPath(path), head, branch, IsMain: entries.Count == 0, bare, locked, prunable));
            }

            path = head = branch = null;
            bare = locked = prunable = false;
        }

        foreach (string raw in output.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            string key = line.Split(' ', 2)[0];
            string value = line.Length > key.Length ? line[(key.Length + 1)..] : string.Empty;
            switch (key)
            {
                case "worktree":
                    Flush();
                    path = value;
                    break;
                case "HEAD":
                    head = value;
                    break;
                case "branch":
                    branch = value.StartsWith("refs/heads/", StringComparison.Ordinal) ? value["refs/heads/".Length..] : value;
                    break;
                case "bare":
                    bare = true;
                    break;
                case "locked":
                    locked = true;
                    break;
                case "prunable":
                    prunable = true;
                    break;
            }
        }

        Flush();
        return entries;
    }

    private async Task<WorktreeFacts> GatherFactsAsync(GitWorktreeEntry entry, CancellationToken cancellationToken)
    {
        bool exists = DevTreeWalker.IsRealDirectory(entry.Path);
        if (entry.IsMain || entry.IsLocked || !exists)
        {
            return new WorktreeFacts(entry.IsMain, entry.IsLocked, exists, null, [], [], null, 0, null);
        }

        // Lock state can change after the list was taken; re-read it from the list right before deciding.
        bool lockedNow = await IsLockedNowAsync(entry, cancellationToken).ConfigureAwait(false);
        if (lockedNow)
        {
            return new WorktreeFacts(false, true, exists, null, [], [], null, 0, null);
        }

        ProcessResult status = await Git(entry.Path, cancellationToken, "status", "--porcelain", "--untracked-files=all", "--ignored=matching").ConfigureAwait(false);
        string? statusError = status.Succeeded ? null : status.ErrorText;
        var changed = new List<string>();
        var ignored = new List<string>();
        foreach (string line in status.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("!!", StringComparison.Ordinal))
            {
                ignored.Add(line[2..].Trim().Trim('"'));
            }
            else
            {
                changed.Add(line);
            }
        }

        ProcessResult unpushed = await Git(entry.Path, cancellationToken, "rev-list", "HEAD", "--not", "--remotes").ConfigureAwait(false);
        string? unpushedError = unpushed.Succeeded ? null : unpushed.ErrorText;
        int unpushedCount = unpushed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;

        DateTime? last = null;
        ProcessResult commitTime = await Git(entry.Path, cancellationToken, "log", "-1", "--format=%ct", "HEAD").ConfigureAwait(false);
        if (commitTime.Succeeded
            && long.TryParse(commitTime.StandardOutput.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
        {
            last = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            DateTime? files = NewestTopLevelTimestamp(entry.Path);
            if (files > last)
            {
                last = files;
            }
        }

        return new WorktreeFacts(false, false, exists, statusError, changed, ignored, unpushedError, unpushedCount, last);
    }

    private async Task<bool> IsLockedNowAsync(GitWorktreeEntry entry, CancellationToken cancellationToken)
    {
        ProcessResult list = await Git(entry.Path, cancellationToken, "worktree", "list", "--porcelain").ConfigureAwait(false);
        if (!list.Succeeded)
        {
            return true;
        }

        StringComparison comparison = JunkRootPlanner.DefaultPathComparison;
        return ParsePorcelain(list.StandardOutput)
            .Where(e => string.Equals(e.Path, entry.Path, comparison))
            .Select(e => e.IsLocked)
            .DefaultIfEmpty(true)
            .First();
    }

    /// <summary>Newest write/creation time among the worktree's top-level files and folders.</summary>
    private static DateTime? NewestTopLevelTimestamp(string path)
    {
        try
        {
            DateTime? newest = null;
            foreach (FileSystemInfo entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                DateTime stamp = JunkSafetyRules.AgeTimestampUtc(entry.LastWriteTimeUtc, entry.CreationTimeUtc);
                if (newest is null || stamp > newest)
                {
                    newest = stamp;
                }
            }

            return newest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unreadable counts as "just used", which keeps the worktree.
            return DateTime.MaxValue;
        }
    }

    private Task<ProcessResult> Git(string directory, CancellationToken cancellationToken, params string[] arguments) =>
        _runner.RunAsync("git", ["-C", directory, .. arguments], null, cancellationToken);

    private static string NormalizeGitPath(string path)
    {
        try
        {
            return JunkRootPlanner.Normalize(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }

    private static string LastSegment(string entry) =>
        System.IO.Path.GetFileName(entry.TrimEnd('/', '\\'));

    private sealed record RemoveAction(string MainPath, GitWorktreeEntry Entry);

    private sealed record PruneAction(string MainPath);
}
