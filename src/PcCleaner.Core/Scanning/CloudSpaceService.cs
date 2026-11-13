using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>Tuning for <see cref="CloudSpaceService"/>.</summary>
/// <param name="LargestFilesPerRoot">How many of a root's largest files a scan returns for display.</param>
/// <param name="SettleTimeout">
/// How long a release waits for providers to drop local copies before reporting the rest as pending.
/// Providers release asynchronously, so a short wait gives an honest figure without blocking the UI for long.
/// </param>
/// <param name="PollInterval">How often the on-disk sizes are measured again while waiting.</param>
public sealed record CloudSpaceOptions(int LargestFilesPerRoot, TimeSpan SettleTimeout, TimeSpan PollInterval)
{
    public static CloudSpaceOptions Default { get; } = new(25, TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(1));
}

/// <summary>
/// Frees disk space held by cloud drives. It returns downloaded files that are safely in the cloud to
/// online-only. The cloud copy is kept, nothing is deleted, and a released file downloads again when opened.
/// </summary>
/// <remarks>
/// The platform parts (finding roots, reading placeholder state, the release call) sit behind
/// <see cref="ICloudDriveScanner"/> and <see cref="ICloudFileReleaser"/>. The decisions are made here:
/// which files are eligible, which requests are allowed, and how much space was actually freed.
/// </remarks>
public sealed class CloudSpaceService
{
    private readonly ICloudDriveScanner _scanner;
    private readonly ICloudFileReleaser _releaser;
    private readonly CloudSpaceOptions _options;
    private readonly TimeProvider _time;

    public CloudSpaceService(ICloudDriveScanner scanner, ICloudFileReleaser releaser)
        : this(scanner, releaser, CloudSpaceOptions.Default, TimeProvider.System)
    {
    }

    public CloudSpaceService(ICloudDriveScanner scanner, ICloudFileReleaser releaser, CloudSpaceOptions options, TimeProvider time)
    {
        _scanner = scanner;
        _releaser = releaser;
        _options = options;
        _time = time;
    }

    public IReadOnlyList<CloudSyncRoot> GetSyncRoots() => _scanner.GetSyncRoots();

    /// <summary>Scans one root and reports what freeing it would reclaim. Read-only.</summary>
    public async Task<CloudRootScan> ScanAsync(
        CloudSyncRoot root,
        TimeSpan minimumAge,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        DateTime now = _time.GetUtcNow().UtcDateTime;

        IReadOnlyList<CloudFileState> files = await _scanner.GetLocalFilesAsync(
            root,
            state => CloudEligibilityRules.EvaluateIgnoringInUse(state, now, minimumAge) == CloudIneligibility.None,
            cancellationToken,
            filesVisited).ConfigureAwait(false);

        var aggregator = new CloudSpaceAggregator(root, _options.LargestFilesPerRoot);
        foreach (CloudFileState file in files)
        {
            aggregator.Add(new CloudFileVerdict(file, CloudEligibilityRules.Evaluate(file, now, minimumAge)));
        }

        return aggregator.Build();
    }

    /// <summary>
    /// Asks the providers to release the given files. Every file is checked again against the eligibility
    /// rules at the moment of release, and skipped if any rule no longer holds.
    /// </summary>
    public async Task<CloudReleaseResult> ReleaseAsync(
        IReadOnlyList<CloudReleaseRequest> requests,
        TimeSpan minimumAge,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesProcessed = null)
    {
        var skipped = new List<CloudReleaseAttempt>();
        var failed = new List<CloudReleaseAttempt>();
        var waiting = new Dictionary<string, long>(PathComparer);

        await Task.Run(() =>
        {
            var seen = new HashSet<string>(PathComparer);
            int processed = 0;

            foreach (CloudReleaseRequest request in requests)
            {
                // Stop between files, never mid-file, and still report the files already requested.
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (!seen.Add(request.Path))
                {
                    continue;
                }

                if (!IsReleasable(request))
                {
                    skipped.Add(new CloudReleaseAttempt(request.Path, CloudReleaseStatus.Skipped, 0, CloudIneligibility.OutsideReleasableRoot));
                }
                else
                {
                    CloudReleaseAttempt attempt = _releaser.TryRelease(
                        request.Path,
                        state => CloudEligibilityRules.Evaluate(state, _time.GetUtcNow().UtcDateTime, minimumAge));

                    switch (attempt.Status)
                    {
                        case CloudReleaseStatus.Requested:
                            waiting[request.Path] = attempt.AllocatedBytesBefore;
                            break;
                        case CloudReleaseStatus.Skipped:
                            skipped.Add(attempt);
                            break;
                        default:
                            failed.Add(attempt);
                            break;
                    }
                }

                filesProcessed?.Report(++processed);
            }
        }, CancellationToken.None).ConfigureAwait(false);

        var released = new List<string>();
        long reclaimed = 0;
        long start = _time.GetTimestamp();

        while (true)
        {
            foreach (string path in waiting.Keys.ToList())
            {
                long before = waiting[path];
                long? after = _releaser.GetAllocatedBytes(path);
                if (after is { } now && now < before)
                {
                    reclaimed += before - now;
                    released.Add(path);
                    waiting.Remove(path);
                }
            }

            if (waiting.Count == 0 || _time.GetElapsedTime(start) >= _options.SettleTimeout || cancellationToken.IsCancellationRequested)
            {
                break;
            }

            await Task.Delay(_options.PollInterval, _time, cancellationToken)
                .ContinueWith(_ => { }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default)
                .ConfigureAwait(false);
        }

        return new CloudReleaseResult(
            reclaimed,
            waiting.Values.Sum(),
            released,
            waiting.Keys.ToList(),
            skipped,
            failed);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// True only when the root allows release and the path is a canonical path strictly inside it. A path
    /// that is not already in canonical form is refused rather than normalised into something else.
    /// </summary>
    public static bool IsReleasable(CloudReleaseRequest request)
    {
        if (!request.Root.CanFreeUp || string.IsNullOrWhiteSpace(request.Path) || string.IsNullOrWhiteSpace(request.Root.Path))
        {
            return false;
        }

        string path;
        string root;
        try
        {
            path = Path.GetFullPath(request.Path);
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.Root.Path));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        if (!string.Equals(path, request.Path, StringComparison.Ordinal))
        {
            return false;
        }

        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!path.StartsWith(root, comparison) || path.Length <= root.Length)
        {
            return false;
        }

        // A drive or volume root keeps its trailing separator ("D:\"); any other root must be followed by one.
        bool rootEndsWithSeparator = root[^1] == Path.DirectorySeparatorChar || root[^1] == Path.AltDirectorySeparatorChar;
        return rootEndsWithSeparator
            || (path.Length > root.Length + 1
                && (path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar));
    }
}
