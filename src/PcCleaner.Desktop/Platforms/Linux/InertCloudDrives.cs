using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// Linux has no cloud sync roots this app knows how to read, so the dashboard's Cloud Drives tool sees none and
/// has nothing to release. Present only so the shared <c>CloudDrivesViewModel</c> can be built.
/// </summary>
public sealed class InertCloudDriveScanner : ICloudDriveScanner
{
    public IReadOnlyList<CloudSyncRoot> GetSyncRoots() => [];

    public Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
        CloudSyncRoot root,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null) =>
        Task.FromResult<IReadOnlyList<CloudFileState>>([]);
}

/// <summary>Never reached while <see cref="InertCloudDriveScanner"/> reports no roots; refuses rather than pretending.</summary>
public sealed class InertCloudFileReleaser : ICloudFileReleaser
{
    public CloudReleaseAttempt TryRelease(string path, Func<CloudFileState, CloudIneligibility> recheck) =>
        new(path, CloudReleaseStatus.Failed, 0, Error: "Cloud drives are not supported on Linux.");

    public long? GetAllocatedBytes(string path) => null;
}
