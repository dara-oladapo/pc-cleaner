using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Tests;

internal static class CloudTestData
{
    public static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    public static string RootPath { get; } = Path.Combine(Path.GetTempPath(), "pccleaner-cloud-fake", "OneDrive");

    public static CloudSyncRoot Root(bool canFreeUp = true, string? path = null) =>
        new("OneDrive!S-1-5-21-1!Personal", "OneDrive - Personal", CloudProvider.OneDrive, path ?? RootPath, canFreeUp);

    /// <summary>A file that passes every rule. Tests change one property at a time with <c>with</c>.</summary>
    public static CloudFileState Eligible(string name = "report.pdf", long bytes = 4096) => new(
        Path: Path.Combine(RootPath, name),
        LogicalBytes: bytes,
        AllocatedBytes: bytes,
        LastAccessUtc: Now.AddDays(-90),
        LastWriteUtc: Now.AddDays(-120),
        IsPlaceholder: true,
        IsHydrated: true,
        HasForeignReparsePoint: false,
        SyncState: CloudSyncState.InSync,
        ModifiedBytes: 0,
        PinState: CloudPinState.Unspecified,
        IsInUse: false,
        IsTransferring: false,
        IsSystemFile: false);
}

internal sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}

internal sealed class FakeCloudDriveScanner(IReadOnlyList<CloudSyncRoot> roots, IReadOnlyList<CloudFileState> files) : ICloudDriveScanner
{
    /// <summary>Paths the caller asked to probe for "open in another program".</summary>
    public List<string> Probed { get; } = [];

    /// <summary>Paths that the probe reports as open.</summary>
    public HashSet<string> OpenFiles { get; } = [];

    public IReadOnlyList<CloudSyncRoot> GetSyncRoots() => roots;

    public Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
        CloudSyncRoot root,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        var result = new List<CloudFileState>();
        foreach (CloudFileState file in files)
        {
            CloudFileState state = file;
            if (shouldProbeInUse(file))
            {
                Probed.Add(file.Path);
                state = file with { IsInUse = OpenFiles.Contains(file.Path) };
            }

            result.Add(state);
        }

        return Task.FromResult<IReadOnlyList<CloudFileState>>(result);
    }
}

/// <summary>
/// Stands in for a provider. A requested file "dehydrates" (its on-disk size drops to 0) after the given
/// number of size measurements, or never when that is null.
/// </summary>
internal sealed class FakeCloudFileReleaser : ICloudFileReleaser
{
    private readonly Dictionary<string, CloudFileState> _current = [];
    private readonly Dictionary<string, int> _measuredAfterRequest = [];

    public FakeCloudFileReleaser(IEnumerable<CloudFileState> files)
    {
        foreach (CloudFileState file in files)
        {
            _current[file.Path] = file;
        }
    }

    public int? DehydrateAfterMeasurements { get; set; } = 0;

    public HashSet<string> FailingPaths { get; } = [];

    /// <summary>Every path the provider was asked to release. This is what a real run would change.</summary>
    public List<string> Requested { get; } = [];

    /// <summary>Simulates something changing between scan and release.</summary>
    public void Change(string path, Func<CloudFileState, CloudFileState> change) => _current[path] = change(_current[path]);

    public CloudReleaseAttempt TryRelease(string path, Func<CloudFileState, CloudIneligibility> recheck)
    {
        if (!_current.TryGetValue(path, out CloudFileState? state))
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: "not found");
        }

        CloudIneligibility reason = recheck(state);
        if (reason != CloudIneligibility.None)
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Skipped, 0, reason);
        }

        if (FailingPaths.Contains(path))
        {
            return new CloudReleaseAttempt(path, CloudReleaseStatus.Failed, 0, Error: "access denied");
        }

        Requested.Add(path);
        _measuredAfterRequest[path] = 0;
        return new CloudReleaseAttempt(path, CloudReleaseStatus.Requested, state.AllocatedBytes);
    }

    public long? GetAllocatedBytes(string path)
    {
        if (!_current.TryGetValue(path, out CloudFileState? state))
        {
            return null;
        }

        if (_measuredAfterRequest.TryGetValue(path, out int measured))
        {
            if (DehydrateAfterMeasurements is { } threshold && measured >= threshold)
            {
                _current[path] = state with { AllocatedBytes = 0, IsHydrated = false };
                return 0;
            }

            _measuredAfterRequest[path] = measured + 1;
        }

        return state.AllocatedBytes;
    }
}
