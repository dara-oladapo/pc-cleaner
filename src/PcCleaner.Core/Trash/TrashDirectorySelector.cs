using System.Globalization;

namespace PcCleaner.Core.Trash;

/// <summary>A per-mount trash directory that may be used, and whether it still has to be created.</summary>
/// <param name="Path">The trash directory itself (it contains <c>files/</c> and <c>info/</c>).</param>
/// <param name="MustCreate">It doesn't exist yet; the caller creates it with mode 0700.</param>
public sealed record TrashDirectoryCandidate(string Path, bool MustCreate);

/// <summary>The per-mount trash directories the spec allows for one top directory, best first.</summary>
/// <param name="Candidates">Try in order; the first that can be created/used wins. Empty means no valid trash.</param>
/// <param name="Rejections">Why each spec location was skipped, for the error shown when nothing is usable.</param>
public sealed record TrashDirectorySelection(
    IReadOnlyList<TrashDirectoryCandidate> Candidates,
    IReadOnlyList<string> Rejections);

/// <summary>
/// Locates the trash for a file that is not on the same device as the home trash, per trash spec 1.0
/// ("Trash directories" — the <c>$topdir/.Trash/$uid</c> and <c>$topdir/.Trash-$uid</c> methods). Pure: the
/// file system is consulted only through the <c>lstat</c> delegate.
/// </summary>
public static class TrashDirectorySelector
{
    /// <summary>
    /// The spec's two per-mount locations, in its order of preference:
    /// <list type="number">
    /// <item><c>$topdir/.Trash/$uid</c> — only if <c>$topdir/.Trash</c> is a real directory (not a symlink)
    /// with the sticky bit set. An admin creates it; a symlink or a missing sticky bit means anyone could
    /// tamper with other users' trash, so the spec says to ignore it.</item>
    /// <item><c>$topdir/.Trash-$uid</c> — used when the first is unavailable or can't be created; created on
    /// demand.</item>
    /// </list>
    /// An existing per-user directory that is a symlink, not a directory, or owned by someone else is
    /// rejected: trashing into it would hand the user's files to whoever controls it.
    /// </summary>
    /// <param name="topDirectory">The mount point of the file system holding the file.</param>
    /// <param name="uid">The current user's id.</param>
    /// <param name="lstat">Returns the entry's status without following symlinks, or null if absent.</param>
    public static TrashDirectorySelection Select(string topDirectory, uint uid, Func<string, FileSystemEntryStatus?> lstat)
    {
        ArgumentException.ThrowIfNullOrEmpty(topDirectory);
        ArgumentNullException.ThrowIfNull(lstat);

        string uidText = uid.ToString(CultureInfo.InvariantCulture);
        var candidates = new List<TrashDirectoryCandidate>();
        var rejections = new List<string>();

        // Method 1: administrator-created $topdir/.Trash with the sticky bit.
        string shared = Path.Combine(topDirectory, ".Trash");
        FileSystemEntryStatus? sharedStatus = lstat(shared);
        if (sharedStatus is null)
        {
            rejections.Add($"{shared} does not exist");
        }
        else if (sharedStatus.IsSymbolicLink)
        {
            rejections.Add($"{shared} is a symbolic link, which the trash spec forbids");
        }
        else if (!sharedStatus.IsDirectory)
        {
            rejections.Add($"{shared} is not a directory");
        }
        else if (!sharedStatus.IsSticky)
        {
            rejections.Add($"{shared} does not have the sticky bit set, which the trash spec requires");
        }
        else
        {
            AddPerUser(Path.Combine(shared, uidText), uid, lstat, candidates, rejections);
        }

        // Method 2: $topdir/.Trash-$uid, created by us if needed.
        AddPerUser(Path.Combine(topDirectory, ".Trash-" + uidText), uid, lstat, candidates, rejections);

        return new TrashDirectorySelection(candidates, rejections);
    }

    private static void AddPerUser(
        string path,
        uint uid,
        Func<string, FileSystemEntryStatus?> lstat,
        List<TrashDirectoryCandidate> candidates,
        List<string> rejections)
    {
        FileSystemEntryStatus? status = lstat(path);
        if (status is null)
        {
            candidates.Add(new TrashDirectoryCandidate(path, MustCreate: true));
        }
        else if (status.IsSymbolicLink)
        {
            rejections.Add($"{path} is a symbolic link");
        }
        else if (!status.IsDirectory)
        {
            rejections.Add($"{path} is not a directory");
        }
        else if (status.OwnerUid != uid)
        {
            rejections.Add($"{path} is owned by another user (uid {status.OwnerUid})");
        }
        else
        {
            candidates.Add(new TrashDirectoryCandidate(path, MustCreate: false));
        }
    }

    /// <summary>
    /// The home trash: <c>$XDG_DATA_HOME/Trash</c>, falling back to <c>~/.local/share/Trash</c>. Per the XDG
    /// base directory spec, a relative or empty <c>XDG_DATA_HOME</c> is invalid and must be ignored.
    /// </summary>
    /// <returns>The directory, or null when neither variable gives an absolute path.</returns>
    public static string? HomeTrashDirectory(string? xdgDataHome, string? home)
    {
        if (!string.IsNullOrEmpty(xdgDataHome) && xdgDataHome.StartsWith('/'))
        {
            return Path.Combine(xdgDataHome, "Trash");
        }

        if (!string.IsNullOrEmpty(home) && home.StartsWith('/'))
        {
            return Path.Combine(home, ".local", "share", "Trash");
        }

        return null;
    }
}
