using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.DevCleanup;

/// <summary>
/// Offers build output and dependency folders (bin, obj, node_modules, target...) of projects nobody has
/// touched for a while. Deletion goes through the junk pipeline (<see cref="JunkRootScanner"/> then
/// <see cref="IJunkCleaner"/>), so it gets the same manifest, link, protected-type and changed-since-scan checks.
/// </summary>
/// <remarks>
/// A folder is offered only if: the project has no file (outside build/dependency folders) modified in
/// <see cref="DevCleanupOptions.ArtifactIdleDays"/>; git says the folder is ignored (outside a git repo it is
/// skipped); git tracks nothing inside it; and it is a real folder, not a symlink or junction.
/// </remarks>
public sealed class BuildArtifactProvider : IDevCleanupProvider
{
    public const string ProviderId = "build-artifacts";

    /// <summary>Project marker file (or extension) to the folders that marker makes regenerable.</summary>
    public static readonly IReadOnlyList<(Func<string, bool> IsMarker, string Ecosystem, string[] Folders)> Markers =
    [
        (n => n.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase), ".NET", ["bin", "obj"]),
        (n => n.Equals("package.json", StringComparison.OrdinalIgnoreCase), "Node", ["node_modules", ".next", ".turbo", ".nuxt"]),
        (n => n.Equals("Cargo.toml", StringComparison.OrdinalIgnoreCase), "Rust", ["target"]),
        (n => n.Equals("build.gradle", StringComparison.OrdinalIgnoreCase) || n.Equals("build.gradle.kts", StringComparison.OrdinalIgnoreCase), "Gradle", ["build", ".gradle"]),
        (n => n.Equals("pyproject.toml", StringComparison.OrdinalIgnoreCase), "Python", [".venv", "__pycache__"]),
    ];

    /// <summary>Every folder name any marker offers; excluded when judging how recently a project was edited.</summary>
    public static readonly IReadOnlySet<string> ArtifactNames =
        new HashSet<string>(Markers.SelectMany(m => m.Folders).Concat(DevTreeWalker.SkippedNames), StringComparer.OrdinalIgnoreCase);

    /// <summary>Dependency folders that take a slow reinstall to get back; a plain build folder is just a rebuild.</summary>
    private static readonly HashSet<string> SlowToRestore = new(StringComparer.OrdinalIgnoreCase) { "node_modules", ".venv", ".gradle" };

    private readonly IProcessRunner _runner;
    private readonly IJunkCleaner _cleaner;
    private readonly Func<DevCleanupOptions> _optionsSource;
    private readonly TimeProvider _time;

    public BuildArtifactProvider(IProcessRunner runner, IJunkCleaner cleaner, DevCleanupOptions options)
        : this(runner, cleaner, () => options, TimeProvider.System)
    {
    }

    public BuildArtifactProvider(IProcessRunner runner, IJunkCleaner cleaner, DevCleanupOptions options, TimeProvider time)
        : this(runner, cleaner, () => options, time)
    {
    }

    /// <param name="optionsSource">Read at the start of each call, so roots edited in Settings apply to the next scan.</param>
    public BuildArtifactProvider(IProcessRunner runner, IJunkCleaner cleaner, Func<DevCleanupOptions> optionsSource, TimeProvider time)
    {
        _runner = runner;
        _cleaner = cleaner;
        _optionsSource = optionsSource;
        _time = time;
    }

    private DevCleanupOptions Options => _optionsSource();

    public string Id => ProviderId;

    public string DisplayName => "Build artifacts";

    public Task<bool> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ToolLocator.IsInstalled("git") && Options.Roots.Any(Directory.Exists), cancellationToken);

    public async Task<DevCleanupReport> MeasureAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<DevCleanupItem>();
        var keptReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        List<(string Project, string Ecosystem, string Folder)> candidates =
            await Task.Run(() => FindCandidates(cancellationToken), cancellationToken).ConfigureAwait(false);

        var idleCache = new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);
        foreach ((string project, string ecosystem, string folder) in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string path = Path.Combine(project, folder);

            (bool offer, string reason) = await EvaluateAsync(project, folder, idleCache, cancellationToken).ConfigureAwait(false);
            if (!offer)
            {
                keptReasons[reason] = keptReasons.GetValueOrDefault(reason) + 1;
                continue;
            }

            long size = await Task.Run(() => DevTreeWalker.MeasureDirectory(path, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (size == 0)
            {
                continue;
            }

            items.Add(new DevCleanupItem(
                Id,
                $"{ecosystem} build output",
                $"{Path.GetFileName(project)}{Path.DirectorySeparatorChar}{folder}",
                reason,
                size,
                SlowToRestore.Contains(folder) ? DevCleanupRisk.Medium : DevCleanupRisk.Low)
            {
                Path = path,
                Payload = new ArtifactAction(project, folder),
            });
        }

        var notes = new List<string>
        {
            $"Looked at {candidates.Count} build/dependency folder(s) under {string.Join(", ", Options.Roots)}."
                + (keptReasons.Count == 0 ? string.Empty : " Kept: " + string.Join(", ", keptReasons.OrderByDescending(k => k.Value).Select(k => $"{k.Value} {k.Key}")) + "."),
            "Deleted folders are emptied, not removed, and are rebuilt by the next build or install.",
        };

        return new DevCleanupReport(Id, DisplayName, items, notes, []);
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
            if (item.Payload is not ArtifactAction action)
            {
                failed.Add((item, "Not a build artifact item."));
                continue;
            }

            // All rules again, right now: the project may have been opened or rebuilt since the scan.
            (bool offer, string reason) = await EvaluateAsync(action.Project, action.Folder, [], cancellationToken).ConfigureAwait(false);
            string root = Path.Combine(action.Project, action.Folder);
            if (!offer)
            {
                failed.Add((item, $"{root}: no longer eligible ({reason}); left alone."));
                continue;
            }

            var planned = new PlannedJunkRoot(JunkRootPlanner.Normalize(root), JunkCategory.BuildArtifacts, item.Title, IsTemp: false, []);
            JunkRootScan scan = await JunkRootScanner.ScanAsync(
                planned, _time.GetUtcNow().UtcDateTime, JunkSafetyRules.DefaultMinimumAge, JunkRootPlanner.DefaultPathComparison, cancellationToken).ConfigureAwait(false);
            if (scan.RootIsLink)
            {
                failed.Add((item, $"{root}: is a link; not followed."));
                continue;
            }

            var junk = new JunkItem(planned.Path, scan.TotalBytes, JunkCategory.BuildArtifacts, item.Title, IsDirectory: true)
            {
                Files = scan.Files,
                Directories = scan.Directories,
            };

            TrashResult result = await _cleaner.CleanAsync([junk], cancellationToken).ConfigureAwait(false);
            freed += result.BytesFreed;
            if (result.SkippedPaths.Count > 0)
            {
                messages.Add($"{root}: {result.SkippedPaths.Count:N0} file(s) skipped (in use, changed, or protected).");
            }

            if (result.AllSucceeded)
            {
                succeeded.Add(item);
            }
            else
            {
                failed.Add((item, $"{root}: {result.FailedPaths.Count:N0} file(s) could not be deleted, e.g. {result.FailedPaths[0].Error}"));
            }
        }

        return new DevCleanupResult(succeeded, failed, freed) { Messages = messages };
    }

    private List<(string Project, string Ecosystem, string Folder)> FindCandidates(CancellationToken cancellationToken)
    {
        var found = new List<(string, string, string)>();
        foreach (string dir in DevTreeWalker.EnumerateDirectories(Options.Roots, Options.MaxDepth, cancellationToken))
        {
            string[] fileNames;
            try
            {
                fileNames = Directory.EnumerateFiles(dir).Select(f => Path.GetFileName(f)).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var offered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((Func<string, bool> isMarker, string ecosystem, string[] folders) in Markers)
            {
                if (!fileNames.Any(isMarker))
                {
                    continue;
                }

                foreach (string folder in folders)
                {
                    if (offered.Add(folder) && Directory.Exists(Path.Combine(dir, folder)))
                    {
                        found.Add((dir, ecosystem, folder));
                    }
                }
            }
        }

        return found;
    }

    /// <summary>The artifact rules, each a reason to keep. Anything that cannot be confirmed means keep.</summary>
    private async Task<(bool Offer, string Reason)> EvaluateAsync(
        string project,
        string folder,
        Dictionary<string, DateTime?> idleCache,
        CancellationToken cancellationToken)
    {
        string path = Path.Combine(project, folder);
        if (!DevTreeWalker.IsRealDirectory(path))
        {
            return (false, "link or missing");
        }

        DateTime now = _time.GetUtcNow().UtcDateTime;
        DateTime threshold = now - TimeSpan.FromDays(Options.ArtifactIdleDays);
        if (!idleCache.TryGetValue(project, out DateTime? newest))
        {
            newest = await Task.Run(
                () => DevTreeWalker.NewestFileTimestampUtc(project, ArtifactNames, threshold, cancellationToken), cancellationToken).ConfigureAwait(false);
            idleCache[project] = newest;
        }

        if (newest is { } stamp && stamp >= threshold)
        {
            return (false, "recently edited");
        }

        ProcessResult ignored = await _runner.RunAsync("git", ["-C", project, "check-ignore", "-q", "--", folder + "/"], null, cancellationToken).ConfigureAwait(false);
        if (ignored.ExitCode == 1)
        {
            return (false, "not git-ignored");
        }

        if (!ignored.Succeeded)
        {
            return (false, "not in a git repo");
        }

        ProcessResult tracked = await _runner.RunAsync("git", ["-C", project, "ls-files", "--", folder], null, cancellationToken).ConfigureAwait(false);
        if (!tracked.Succeeded || tracked.StandardOutput.Trim().Length > 0)
        {
            return (false, "has tracked files");
        }

        string idle = newest is { } last ? $"project idle {(int)(now - last).TotalDays} days" : "project has no source files";
        return (true, $"{idle}, git-ignored");
    }

    private sealed record ArtifactAction(string Project, string Folder);
}
