namespace PcCleaner.Core.Models;

/// <summary>A file and the verdict on it.</summary>
public sealed record CloudFileVerdict(CloudFileState File, CloudIneligibility Reason)
{
    public bool IsEligible => Reason == CloudIneligibility.None;
}

/// <summary>The result of scanning one sync root.</summary>
/// <param name="Root">The root that was scanned.</param>
/// <param name="HydratedBytes">On-disk bytes of every downloaded file in the root.</param>
/// <param name="HydratedCount">How many downloaded files there are.</param>
/// <param name="EligibleBytes">On-disk bytes that releasing the eligible files would free.</param>
/// <param name="EligibleCount">How many files are eligible.</param>
/// <param name="Eligible">Every eligible file. This is the list a release works from.</param>
/// <param name="LargestFiles">The largest downloaded files, eligible or not, biggest first.</param>
/// <param name="IneligibleBytesByReason">Downloaded bytes that will stay, by the reason they stay.</param>
public sealed record CloudRootScan(
    CloudSyncRoot Root,
    long HydratedBytes,
    int HydratedCount,
    long EligibleBytes,
    int EligibleCount,
    IReadOnlyList<CloudFileState> Eligible,
    IReadOnlyList<CloudFileVerdict> LargestFiles,
    IReadOnlyDictionary<CloudIneligibility, long> IneligibleBytesByReason);
