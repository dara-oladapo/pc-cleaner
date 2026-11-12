using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Desktop.Platforms.Linux;

/// <summary>
/// An <see cref="IJunkCleaner"/> that deletes nothing and says so. Stands in for Core's <c>JunkCleaner</c>
/// until the real Linux scanner (#10) lands.
/// </summary>
/// <remarks>
/// Every item is reported back as failed with the reason, so the junk screen keeps the rows and states that
/// nothing was removed, rather than pretending space was freed.
/// </remarks>
public sealed class InertJunkCleaner : IJunkCleaner
{
    public const string Reason = "Cleaning is not enabled in this build yet (see #10).";

    public Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TrashResult([], items.Select(i => (i.Path, Reason)).ToList(), 0));
}
