using PcCleaner.Core.Models;

namespace PcCleaner.Core.Abstractions;

/// <summary>Platform-specific: moves files/folders to the OS trash/recycle bin rather than deleting permanently.</summary>
public interface IFileTrasher
{
    Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}
