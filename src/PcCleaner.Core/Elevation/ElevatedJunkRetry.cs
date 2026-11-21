using PcCleaner.Core.Abstractions;

namespace PcCleaner.Core.Elevation;

/// <summary>What an elevated junk retry achieved, measured on disk rather than taken from the helper's word.</summary>
public sealed record ElevatedJunkOutcome(
    ElevationStatus Status,
    IReadOnlyList<string> RemovedPaths,
    IReadOnlyList<string> StillFailedPaths,
    long BytesFreed);

/// <summary>
/// Retries a junk clean's failures with elevated rights. The elevated helper can only report an exit code, so the
/// sizes of the failed files are read before the retry and their absence checked after, which gives the UI an honest
/// "freed" figure and the list of what is still left.
/// </summary>
public static class ElevatedJunkRetry
{
    public static async Task<ElevatedJunkOutcome> RunAsync(
        IElevationService elevation,
        IReadOnlyList<string> roots,
        IReadOnlyList<string> failedPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(elevation);

        var sizes = failedPaths.Distinct(StringComparer.Ordinal).ToDictionary(p => p, TryGetSize, StringComparer.Ordinal);

        ElevationResult result = await elevation.CleanJunkRootsAsync(roots, cancellationToken);

        // Only a helper that ran to completion is worth checking the disk for. A failed, rejected or cancelled run is
        // reported as freeing nothing rather than guessing from whatever happens to be missing now.
        if (result.Status is not (ElevationStatus.Succeeded or ElevationStatus.PartiallySucceeded))
        {
            return new ElevatedJunkOutcome(result.Status, [], [.. sizes.Keys], 0);
        }

        var removed = new List<string>();
        var stillFailed = new List<string>();
        long bytesFreed = 0;

        foreach ((string path, long size) in sizes)
        {
            if (IsConfirmedRemoved(path))
            {
                removed.Add(path);
                bytesFreed += size;
            }
            else
            {
                stillFailed.Add(path);
            }
        }

        return new ElevatedJunkOutcome(result.Status, removed, stillFailed, bytesFreed);
    }

    /// <summary>
    /// True only when the OS positively says the path is gone (file or directory not found). Access denied, I/O errors
    /// or a path that still exists all count as not removed, so the "freed" figure never overstates.
    /// </summary>
    public static bool IsConfirmedRemoved(string path)
    {
        try
        {
            File.GetAttributes(path);
            return false;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static long TryGetSize(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
