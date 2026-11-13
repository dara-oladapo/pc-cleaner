using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>A folder the scan will not walk, and the sentence that tells the user why.</summary>
public sealed record SkippedScanRoot(string Path, string Reason);

/// <summary>What a large-file or duplicate scan will actually walk, after the volume setting is applied.</summary>
/// <param name="Roots">Folders to walk, with nothing nested inside another.</param>
/// <param name="Skipped">Folders the user listed that sit on a volume that is not selected or not there.</param>
/// <param name="UnavailableVolumes">Selected volumes that are not mounted right now. They stay selected.</param>
public sealed record ScanPlan(
    IReadOnlyList<string> Roots,
    IReadOnlyList<SkippedScanRoot> Skipped,
    IReadOnlyList<string> UnavailableVolumes);

/// <summary>A row in the "Volumes to scan" setting: a mounted volume, or a selected one that has gone missing.</summary>
/// <param name="Volume">Null when the volume is not mounted right now.</param>
public sealed record VolumeChoice(string RootPath, ScanVolume? Volume, bool IsSelected)
{
    public bool IsAvailable => Volume is not null;
}

/// <summary>
/// Applies the "Volumes to scan" setting to the folder list a tool was given. Pure: no I/O, so every rule
/// below is unit tested against a fake <see cref="VolumeSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// The system volume is scanned through the tool's folder list (Documents, Downloads, …), never from its
/// root. Walking all of C:\ or / would put the OS, page file and installed apps in front of a user holding a
/// "Move to Recycle Bin" button, which is the wrong trade for a cleaner.
/// </para>
/// <para>
/// Any other selected volume has no such folders, so it is walked from its root. Volumes are walked whole
/// because that is what selecting a data disk means.
/// </para>
/// <para>
/// A folder on a volume that is not selected — or not mounted — is skipped with a reason rather than
/// silently dropped, so the result never looks like "nothing there" when the truth is "didn't look".
/// </para>
/// </remarks>
public static class ScanVolumePlanner
{
    /// <summary>The selection used before the user has ever changed the setting.</summary>
    public static IReadOnlyList<string> DefaultSelection(VolumeSnapshot snapshot)
    {
        var defaults = snapshot.Volumes.Where(v => v.IsSelectedByDefault).Select(v => v.RootPath).ToList();
        return defaults.Count > 0 ? defaults : [snapshot.SystemRoot];
    }

    /// <summary>The rows for the setting: every choosable volume, then any selected volume that is missing.</summary>
    public static IReadOnlyList<VolumeChoice> ListChoices(VolumeSnapshot snapshot, IReadOnlyCollection<string> selectedRoots)
    {
        var choices = snapshot.Volumes
            .Select(v => new VolumeChoice(v.RootPath, v, Contains(selectedRoots, v.RootPath, snapshot.PathComparison)))
            .ToList();

        foreach (string root in selectedRoots)
        {
            if (!choices.Any(c => SameRoot(c.RootPath, root, snapshot.PathComparison)))
            {
                // Kept, ticked, and shown as unavailable: a backup disk that's unplugged today is still the
                // disk the user wants scanned when it's back.
                choices.Add(new VolumeChoice(root, Volume: null, IsSelected: true));
            }
        }

        return choices;
    }

    public static ScanPlan Plan(
        IReadOnlyList<string> folderRoots,
        VolumeSnapshot snapshot,
        IReadOnlyCollection<string> selectedRoots)
    {
        StringComparison comparison = snapshot.PathComparison;

        var selectedAvailable = snapshot.Volumes
            .Where(v => Contains(selectedRoots, v.RootPath, comparison))
            .ToList();

        var unavailable = selectedRoots
            .Where(r => !snapshot.Volumes.Any(v => SameRoot(v.RootPath, r, comparison)))
            .Distinct(StringComparer.FromComparison(comparison))
            .ToList();

        var roots = new List<string>();
        var skipped = new List<SkippedScanRoot>();

        foreach (string folder in folderRoots)
        {
            string? mount = ContainingMount(folder, snapshot, selectedRoots);
            ScanVolume? volume = mount is null
                ? null
                : snapshot.Volumes.FirstOrDefault(v => SameRoot(v.RootPath, mount, comparison));

            if (volume is not null && selectedAvailable.Contains(volume))
            {
                roots.Add(folder);
            }
            else if (volume is not null)
            {
                skipped.Add(new SkippedScanRoot(folder, $"{volume.DisplayName} is not selected in Settings."));
            }
            else if (mount is not null && Contains(selectedRoots, mount, comparison))
            {
                skipped.Add(new SkippedScanRoot(folder, $"{mount} is not connected."));
            }
            else
            {
                skipped.Add(new SkippedScanRoot(folder, "It is not on a local volume PC Cleaner scans."));
            }
        }

        foreach (ScanVolume volume in selectedAvailable.Where(v => !v.IsSystem))
        {
            roots.Add(volume.RootPath);
        }

        return new ScanPlan(RemoveNested(roots, comparison), skipped, unavailable);
    }

    /// <summary>
    /// The mount point a path lives on: the longest mount root that contains it. Selected roots are
    /// included so a folder on a volume that's gone missing is still attributed to it, not to "/".
    /// </summary>
    private static string? ContainingMount(string path, VolumeSnapshot snapshot, IReadOnlyCollection<string> selectedRoots)
    {
        return snapshot.MountRoots
            .Concat(snapshot.Volumes.Select(v => v.RootPath))
            .Concat(selectedRoots)
            .Where(root => IsUnder(path, root, snapshot.PathComparison))
            .OrderByDescending(root => VolumeCatalog.TrimEndSeparator(root).Length)
            .FirstOrDefault();
    }

    /// <summary>Drops any root inside another, so a file is never walked (and counted as a duplicate of itself) twice.</summary>
    private static List<string> RemoveNested(List<string> roots, StringComparison comparison)
    {
        var distinct = roots.Distinct(StringComparer.FromComparison(comparison)).ToList();
        return distinct
            .Where(r => !distinct.Any(other => !SameRoot(other, r, comparison) && IsUnder(r, other, comparison)))
            .ToList();
    }

    internal static bool IsUnder(string path, string root, StringComparison comparison)
    {
        string p = VolumeCatalog.TrimEndSeparator(path);
        string r = VolumeCatalog.TrimEndSeparator(root);

        if (string.Equals(p, r, comparison))
        {
            return true;
        }

        if (r == "/")
        {
            return p.StartsWith('/');
        }

        return p.Length > r.Length
            && p.StartsWith(r, comparison)
            && (p[r.Length] == '\\' || p[r.Length] == '/');
    }

    private static bool SameRoot(string a, string b, StringComparison comparison) =>
        string.Equals(VolumeCatalog.TrimEndSeparator(a), VolumeCatalog.TrimEndSeparator(b), comparison);

    private static bool Contains(IReadOnlyCollection<string> roots, string root, StringComparison comparison) =>
        roots.Any(r => SameRoot(r, root, comparison));
}
