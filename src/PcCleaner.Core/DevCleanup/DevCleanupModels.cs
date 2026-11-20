namespace PcCleaner.Core.DevCleanup;

/// <summary>How much could be lost if the user is wrong about an item. Drives the badge and the default selection.</summary>
public enum DevCleanupRisk
{
    /// <summary>Regenerable cache with nothing of the user's in it.</summary>
    Low,

    /// <summary>Regenerable, but costs a slow rebuild or re-download, or relies on a heuristic.</summary>
    Medium,

    /// <summary>May contain user data. Never selected by default; always carries a <see cref="DevCleanupItem.Warning"/>.</summary>
    High,
}

/// <summary>One thing a developer cleanup provider offers to remove.</summary>
/// <param name="ProviderId">The <see cref="IDevCleanupProvider.Id"/> that produced it and must clean it.</param>
/// <param name="Group">Heading it is listed under, e.g. "Docker (WSL: Ubuntu)".</param>
/// <param name="Title">Short name, e.g. "Build cache older than 7 days".</param>
/// <param name="Reason">Why it is offered, e.g. "worktree clean, pushed, idle 41 days".</param>
/// <param name="SizeBytes">Bytes it should free, or null when the engine could not say.</param>
public sealed record DevCleanupItem(
    string ProviderId,
    string Group,
    string Title,
    string Reason,
    long? SizeBytes,
    DevCleanupRisk Risk)
{
    /// <summary>A stable key for this item within one measurement, for matching results back to rows.</summary>
    public string Key { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>The folder involved, when there is one.</summary>
    public string? Path { get; init; }

    /// <summary>True when <see cref="SizeBytes"/> is an upper bound ("up to") rather than an exact figure.</summary>
    public bool SizeIsEstimate { get; init; }

    public bool SelectedByDefault { get; init; }

    /// <summary>Shown prominently before the user can select it. Set for every <see cref="DevCleanupRisk.High"/> item.</summary>
    public string? Warning { get; init; }

    /// <summary>Provider-private data needed to clean the item. Opaque to everything else.</summary>
    public object? Payload { get; init; }
}

/// <summary>A virtual disk on the host, reported read-only so the user can see what a prune will not shrink.</summary>
public sealed record VirtualDiskInfo(string Name, string Path, long SizeBytes);

/// <summary>What one provider found. Errors are tool output to show the user, never exceptions.</summary>
public sealed record DevCleanupReport(
    string ProviderId,
    string DisplayName,
    IReadOnlyList<DevCleanupItem> Items,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> Errors)
{
    public IReadOnlyList<VirtualDiskInfo> VirtualDisks { get; init; } = [];

    public static DevCleanupReport Empty(string providerId, string displayName) => new(providerId, displayName, [], [], []);
}

/// <summary>The outcome of cleaning a set of items.</summary>
public sealed record DevCleanupResult(
    IReadOnlyList<DevCleanupItem> Succeeded,
    IReadOnlyList<(DevCleanupItem Item, string Error)> Failed,
    long BytesFreed)
{
    /// <summary>Tool output worth showing (e.g. "Total reclaimed space: 12GB"), and files the delete pipeline skipped.</summary>
    public IReadOnlyList<string> Messages { get; init; } = [];

    public static DevCleanupResult Combine(IEnumerable<DevCleanupResult> results)
    {
        var list = results.ToList();
        return new DevCleanupResult(
            list.SelectMany(r => r.Succeeded).ToList(),
            list.SelectMany(r => r.Failed).ToList(),
            list.Sum(r => r.BytesFreed))
        {
            Messages = list.SelectMany(r => r.Messages).ToList(),
        };
    }
}

/// <summary>
/// One source of developer disk usage (Docker, git worktrees, build output). Implementations never throw for
/// tool failures: a broken engine or repository becomes an entry in <see cref="DevCleanupReport.Errors"/> or
/// <see cref="DevCleanupResult.Failed"/>. Only cancellation propagates.
/// </summary>
public interface IDevCleanupProvider
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Cheap check that the provider has anything to look at on this machine.</summary>
    Task<bool> DetectAsync(CancellationToken cancellationToken = default);

    /// <summary>Measures and returns what could be cleaned. Read-only.</summary>
    Task<DevCleanupReport> MeasureAsync(CancellationToken cancellationToken = default);

    /// <summary>Cleans exactly the given items (all from this provider), re-checking each one first.</summary>
    Task<DevCleanupResult> CleanAsync(IReadOnlyList<DevCleanupItem> items, CancellationToken cancellationToken = default);
}
