namespace PcCleaner.Core.Models;

public sealed record TrashResult(
    IReadOnlyList<string> SucceededPaths,
    IReadOnlyList<(string Path, string Error)> FailedPaths,
    long BytesFreed)
{
    /// <summary>
    /// Paths deliberately left alone: in use by another program, changed since the scan, or outside the scanned
    /// root. These are not failures — retrying with more rights would not (and should not) remove them.
    /// </summary>
    public IReadOnlyList<(string Path, string Reason)> SkippedPaths { get; init; } = [];

    public bool AllSucceeded => FailedPaths.Count == 0;
}
