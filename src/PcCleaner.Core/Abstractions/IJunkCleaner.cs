using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Hard-deletes junk/cache items (regenerable data, unlike user files, so no trash/recycle-bin round-trip).</summary>
public interface IJunkCleaner
{
    Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default);
}
