namespace PcCleaner.Core.Models;

/// <summary>
/// One place a platform scanner looks for junk. <paramref name="IsTemp"/> marks a shared temp folder (user or
/// system %TEMP%, $TMPDIR): those hold files that running programs are still using, so they get the stricter
/// rules in <see cref="Scanning.JunkSafetyRules"/> — only old files, no recently touched folders, no `claude`
/// folder at the top level.
/// </summary>
public sealed record JunkSource(string Path, JunkCategory Category, string Description, bool IsTemp = false);

/// <summary>
/// A <see cref="JunkSource"/> after overlap planning: its path is normalised, and <see cref="ExcludedPaths"/> lists
/// the nested roots that are scanned (and offered) on their own, so this root's walk must not enter them.
/// </summary>
public sealed record PlannedJunkRoot(
    string Path,
    JunkCategory Category,
    string Description,
    bool IsTemp,
    IReadOnlyList<string> ExcludedPaths);
