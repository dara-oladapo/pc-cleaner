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

        if (result.Status is ElevationStatus.Cancelled or ElevationStatus.Rejected)
        {
            return new ElevatedJunkOutcome(result.Status, [], [.. sizes.Keys], 0);
        }

        var removed = new List<string>();
        var stillFailed = new List<string>();
        long bytesFreed = 0;

        foreach ((string path, long size) in sizes)
        {
            if (File.Exists(path))
            {
                stillFailed.Add(path);
            }
            else
            {
                removed.Add(path);
                bytesFreed += size;
            }
        }

        return new ElevatedJunkOutcome(result.Status, removed, stillFailed, bytesFreed);
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
