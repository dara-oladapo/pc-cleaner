using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>
/// Asks a cloud provider to return one file to online-only. The cloud copy is kept; only the local copy is
/// released. Nothing is deleted.
/// </summary>
/// <remarks>
/// Use <c>CloudSpaceService.ReleaseAsync</c> rather than calling this directly. It checks that every path
/// belongs to a root that allows release, and it measures what was actually freed.
/// </remarks>
public interface ICloudFileReleaser
{
    /// <summary>
    /// Reads the file's current state, and asks the provider to release it only if <paramref name="recheck"/>
    /// returns <see cref="CloudIneligibility.None"/> for that state.
    /// </summary>
    /// <remarks>
    /// The state passed to <paramref name="recheck"/> must be read at the time of the call, not taken from the
    /// scan. A file that changed, was pinned, or was opened since the scan must be skipped.
    /// </remarks>
    CloudReleaseAttempt TryRelease(string path, Func<CloudFileState, CloudIneligibility> recheck);

    /// <summary>The bytes the file occupies on disk right now, or null when it cannot be read.</summary>
    /// <remarks>Must not download the file.</remarks>
    long? GetAllocatedBytes(string path);
}
