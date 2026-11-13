using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>
/// Finds cloud sync roots and reports the local files in them. Read-only: nothing here changes a file.
/// </summary>
public interface ICloudDriveScanner
{
    /// <summary>The current user's cloud sync roots that exist on disk.</summary>
    IReadOnlyList<CloudSyncRoot> GetSyncRoots();

    /// <summary>
    /// Reports every downloaded file under <paramref name="root"/>. Files whose content is not on this
    /// device are not reported, and must not be opened in a way that downloads them.
    /// </summary>
    /// <param name="root">The root to walk.</param>
    /// <param name="shouldProbeInUse">
    /// Called with a file's state before <see cref="CloudFileState.IsInUse"/> is filled in. Return true to have
    /// the scanner check whether another program has the file open. Probing briefly opens the file, so the
    /// caller limits it to files that pass every other rule.
    /// </param>
    /// <param name="cancellationToken">Stops the walk.</param>
    /// <param name="filesVisited">Running count of files looked at, for progress.</param>
    Task<IReadOnlyList<CloudFileState>> GetLocalFilesAsync(
        CloudSyncRoot root,
        Func<CloudFileState, bool> shouldProbeInUse,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null);
}
