using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>Adds up one root's files into a <see cref="CloudRootScan"/>.</summary>
public sealed class CloudSpaceAggregator
{
    private readonly CloudSyncRoot _root;
    private readonly int _largestCount;
    private readonly List<CloudFileState> _eligible = [];
    private readonly Dictionary<CloudIneligibility, long> _ineligibleBytes = [];

    // Min-heap on size, so the smallest of the current top N is the one replaced.
    private readonly PriorityQueue<CloudFileVerdict, long> _largest = new();

    private long _hydratedBytes;
    private int _hydratedCount;
    private long _eligibleBytes;

    public CloudSpaceAggregator(CloudSyncRoot root, int largestCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(largestCount);
        _root = root;
        _largestCount = largestCount;
    }

    /// <summary>Adds one file. Files that are not downloaded are ignored: they occupy no space to free.</summary>
    public void Add(CloudFileVerdict verdict)
    {
        CloudFileState file = verdict.File;
        if (!file.IsHydrated || file.AllocatedBytes <= 0 || verdict.Reason == CloudIneligibility.NotHydrated)
        {
            return;
        }

        _hydratedBytes += file.AllocatedBytes;
        _hydratedCount++;

        if (verdict.IsEligible)
        {
            _eligibleBytes += file.AllocatedBytes;
            _eligible.Add(file);
        }
        else
        {
            _ineligibleBytes[verdict.Reason] = _ineligibleBytes.GetValueOrDefault(verdict.Reason) + file.AllocatedBytes;
        }

        if (_largestCount == 0)
        {
            return;
        }

        if (_largest.Count < _largestCount)
        {
            _largest.Enqueue(verdict, file.AllocatedBytes);
        }
        else if (_largest.TryPeek(out _, out long smallest) && file.AllocatedBytes > smallest)
        {
            _largest.DequeueEnqueue(verdict, file.AllocatedBytes);
        }
    }

    public CloudRootScan Build()
    {
        var largest = _largest.UnorderedItems
            .Select(i => i.Element)
            .OrderByDescending(v => v.File.AllocatedBytes)
            .ThenBy(v => v.File.Path, StringComparer.Ordinal)
            .ToList();

        return new CloudRootScan(
            _root,
            _hydratedBytes,
            _hydratedCount,
            _eligibleBytes,
            _eligible.Count,
            _eligible.ToList(),
            largest,
            new Dictionary<CloudIneligibility, long>(_ineligibleBytes));
    }
}
