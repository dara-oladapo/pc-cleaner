using PcCleaner.Core.Utilities;

namespace PcCleaner.App.Services;

/// <summary>
/// Builds the short list of what a destructive action is about to remove, biggest first.
/// </summary>
/// <remarks>
/// A confirmation that only says "delete 14 items?" gets dismissed reflexively — there's nothing in it to
/// check against. Naming the largest few, with their sizes, is what lets someone notice the one entry that
/// shouldn't be there. Capped deliberately: a dialog listing forty paths is as unreadable as one listing none.
/// </remarks>
internal static class DialogManifest
{
    private const int MaxLines = 4;

    public static IReadOnlyList<string> Build(IEnumerable<(long Bytes, string Label)> items)
    {
        var ordered = items.OrderByDescending(i => i.Bytes).ToList();

        var lines = ordered
            .Take(MaxLines)
            .Select(i => $"{ByteSizeFormatter.Format(i.Bytes),9}   {i.Label}")
            .ToList();

        if (ordered.Count > MaxLines)
        {
            lines.Add($"+ {ordered.Count - MaxLines} more");
        }

        return lines;
    }
}
