using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Resolves overlaps between junk sources so every file is counted and offered exactly once.
/// <para>
/// Two kinds of overlap occur in practice: the same folder listed twice (<c>Path.GetTempPath()</c> and
/// <c>%LOCALAPPDATA%\Temp</c>), and one folder nested in another (<c>Temp\NuGetScratch</c> inside <c>Temp</c>;
/// Safari, Homebrew and pip caches inside <c>~/Library/Caches</c>). Exact duplicates keep the first entry. For
/// nesting, the child keeps its own, more specific label and the parent's walk skips the child's folder.
/// A child nested in a temp root inherits the temp rules, so nesting never loosens safety. In the same spirit a
/// root is pre-selected only if every source that names it, or contains it, is.
/// </para>
/// </summary>
public static class JunkRootPlanner
{
    /// <summary>Case-insensitive where the default file system is (Windows NTFS, macOS APFS); exact elsewhere.</summary>
    public static StringComparison DefaultPathComparison =>
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    /// <summary>Full path with no trailing separator (except for a bare root such as <c>C:\</c> or <c>/</c>).</summary>
    public static string Normalize(string path)
    {
        string full = Path.GetFullPath(path);
        string trimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? root = Path.GetPathRoot(full);
        return trimmed.Length == 0 || (root is not null && trimmed.Length < root.Length) ? root ?? full : trimmed;
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="root"/> or lies inside it. Both must be normalised.</summary>
    public static bool IsSameOrUnder(string path, string root, StringComparison comparison)
    {
        if (string.Equals(path, root, comparison))
        {
            return true;
        }

        string prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, comparison);
    }

    /// <summary>True when <paramref name="path"/> lies inside <paramref name="root"/> and is not the root itself.</summary>
    public static bool IsStrictlyUnder(string path, string root, StringComparison comparison) =>
        !string.Equals(path, root, comparison) && IsSameOrUnder(path, root, comparison);

    /// <param name="includeDeveloperSources">
    /// False while developer mode is off. Sources with <see cref="JunkSource.RequiresDeveloperMode"/> are then
    /// not planned (so not scanned or offered), but they still count as nested roots: a parent such as
    /// <c>~/Library/Caches</c> keeps skipping their folders. Dropping them before planning would let the parent's
    /// walk re-absorb a developer cache and offer it pre-ticked under the parent's label.
    /// </param>
    public static IReadOnlyList<PlannedJunkRoot> Plan(
        IEnumerable<JunkSource> sources,
        StringComparison comparison,
        bool includeDeveloperSources = true)
    {
        // Exact duplicates: first entry wins, but if either copy is a temp root the survivor is too, and if either
        // copy is opt-in the survivor is too.
        var unique = new List<JunkSource>();
        foreach (JunkSource source in sources)
        {
            var normalized = source with { Path = Normalize(source.Path) };
            int existing = unique.FindIndex(u => string.Equals(u.Path, normalized.Path, comparison));
            if (existing < 0)
            {
                unique.Add(normalized);
            }
            else
            {
                unique[existing] = unique[existing] with
                {
                    IsTemp = unique[existing].IsTemp || normalized.IsTemp,
                    SelectedByDefault = unique[existing].SelectedByDefault && normalized.SelectedByDefault,

                    // Developer-only only if every source naming the folder is; otherwise it is ordinary junk.
                    RequiresDeveloperMode = unique[existing].RequiresDeveloperMode && normalized.RequiresDeveloperMode,
                };
            }
        }

        var planned = new List<PlannedJunkRoot>(unique.Count);
        foreach (JunkSource source in unique)
        {
            // Exclusion-only: not planned itself, but still in `unique`, so it lands in its parents' ExcludedPaths.
            if (source.RequiresDeveloperMode && !includeDeveloperSources)
            {
                continue;
            }

            bool isTemp = source.IsTemp
                || unique.Any(parent => parent.IsTemp && IsStrictlyUnder(source.Path, parent.Path, comparison));

            bool selectedByDefault = source.SelectedByDefault
                && unique.All(parent => parent.SelectedByDefault || !IsStrictlyUnder(source.Path, parent.Path, comparison));

            var excluded = unique
                .Where(child => IsStrictlyUnder(child.Path, source.Path, comparison))
                .Select(child => child.Path)
                .ToList();

            planned.Add(new PlannedJunkRoot(source.Path, source.Category, source.Description, isTemp, excluded, selectedByDefault));
        }

        return planned;
    }
}
