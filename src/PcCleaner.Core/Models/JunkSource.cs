namespace PcCleaner.Core.Models;

/// <summary>
/// One place a platform scanner looks for junk. <paramref name="IsTemp"/> marks a shared temp folder (user or
/// system %TEMP%, $TMPDIR): those hold files that running programs are still using, so they get the stricter
/// rules in <see cref="Scanning.JunkSafetyRules"/> — only old files, no recently touched folders, no `claude`
/// folder at the top level.
/// <para>
/// <paramref name="SelectedByDefault"/> is false for caches that are large and slow to rebuild (installed package
/// stores, downloaded toolchains, device symbols): they are listed so the user can see the space, but are only
/// cleaned if the user ticks them. <paramref name="RequiresDeveloperMode"/> marks developer tool caches, which a
/// "Developer mode" setting decides whether to offer at all.
/// </para>
/// </summary>
public sealed record JunkSource(
    string Path,
    JunkCategory Category,
    string Description,
    bool IsTemp = false,
    bool SelectedByDefault = true,
    bool RequiresDeveloperMode = false);

/// <summary>
/// A <see cref="JunkSource"/> after overlap planning: its path is normalised, and <see cref="ExcludedPaths"/> lists
/// the nested roots that are scanned (and offered) on their own, so this root's walk must not enter them.
/// </summary>
public sealed record PlannedJunkRoot(
    string Path,
    JunkCategory Category,
    string Description,
    bool IsTemp,
    IReadOnlyList<string> ExcludedPaths,
    bool SelectedByDefault = true);
