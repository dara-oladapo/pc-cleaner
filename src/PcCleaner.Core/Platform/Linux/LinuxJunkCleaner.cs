using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Platform.Linux;

/// <summary>
/// The Linux junk cleaner: <see cref="FreedesktopTrashEmptier"/> for the home trash, and the shared cleaner
/// (Core's <c>JunkCleaner</c>) for every other row.
/// </summary>
/// <remarks>
/// The trash can't go through the shared cleaner: that one deletes every file and then every empty
/// subdirectory, which would remove <c>Trash/files</c> and <c>Trash/info</c> themselves and could delete an
/// entry's <c>.trashinfo</c> while the entry itself survived.
/// </remarks>
public sealed class LinuxJunkCleaner(IJunkCleaner inner) : IJunkCleaner
{
    // TODO(#10 follow-up): re-check the mount table at clean time. The scanner only offers roots with no other
    // device mounted inside them, but Core's JunkCleaner walks a root again when cleaning and does not check
    // whether a mount appeared since. See also FreedesktopTrashEmptier.EmptyAsync.
    public async Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);

        var trashes = items.Where(IsHomeTrash).ToList();
        var others = items.Where(i => !IsHomeTrash(i)).ToList();

        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        if (others.Count > 0)
        {
            Merge(await inner.CleanAsync(others, cancellationToken));
        }

        foreach (JunkItem trash in trashes)
        {
            Merge(await FreedesktopTrashEmptier.EmptyAsync(trash.Path, cancellationToken));
        }

        return new TrashResult(succeeded, failed, bytesFreed);

        void Merge(TrashResult result)
        {
            succeeded.AddRange(result.SucceededPaths);
            failed.AddRange(result.FailedPaths);
            bytesFreed += result.BytesFreed;
        }
    }

    private static bool IsHomeTrash(JunkItem item) =>
        item.Category == JunkCategory.TrashBin && item.IsDirectory;
}
