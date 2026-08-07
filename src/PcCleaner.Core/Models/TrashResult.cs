namespace PcCleaner.Core.Models;

public sealed record TrashResult(
    IReadOnlyList<string> SucceededPaths,
    IReadOnlyList<(string Path, string Error)> FailedPaths,
    long BytesFreed)
{
    public bool AllSucceeded => FailedPaths.Count == 0;
}
