using System.Security;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Scanning;

/// <summary>
/// Says whether a path lives inside a cloud drive folder (OneDrive, Dropbox, iCloud Drive, Synology Drive...).
/// </summary>
/// <remarks>
/// Anything inside a cloud drive is "cloud only" as far as the Large Files and Duplicates tools are concerned:
/// it is marked, it cannot be ticked, and it is never moved to the Recycle Bin from there. Deleting a synced
/// file deletes it everywhere the drive syncs to, and a file that is only a cloud placeholder reports its full
/// size while using no disk, so its size says nothing about what deleting it would free. The place to free
/// space held by a cloud drive is the Cloud Drives tool, which makes files online-only and deletes nothing.
/// </remarks>
public sealed class CloudLocationGuard
{
    private readonly ICloudDriveScanner _scanner;
    private readonly StringComparison _comparison;
    private IReadOnlyList<string> _roots = [];
    private IReadOnlyDictionary<string, CloudSyncRoot> _byRoot = new Dictionary<string, CloudSyncRoot>();

    public CloudLocationGuard(ICloudDriveScanner scanner)
        : this(scanner, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
    {
    }

    /// <summary>The comparison is a parameter so a test can pick it; on disk it follows the platform's usual case rules.</summary>
    public CloudLocationGuard(ICloudDriveScanner scanner, StringComparison comparison)
    {
        _scanner = scanner;
        _comparison = comparison;
    }

    /// <summary>The cloud folders found by the last <see cref="Refresh"/>.</summary>
    public IReadOnlyList<CloudSyncRoot> Roots => _byRoot.Values.ToList();

    /// <summary>
    /// Re-reads the cloud folders. If they cannot be read, the previous list stays, so a transient failure
    /// does not make a cloud file look like an ordinary one.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<CloudSyncRoot> found;
        try
        {
            found = _scanner.GetSyncRoots();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return;
        }

        var byRoot = new Dictionary<string, CloudSyncRoot>(StringComparer.FromComparison(_comparison));
        foreach (CloudSyncRoot root in found)
        {
            string path = TrimSeparator(root.Path);
            if (path.Length > 0)
            {
                byRoot.TryAdd(path, root);
            }
        }

        _byRoot = byRoot;
        _roots = byRoot.Keys.ToList();
    }

    /// <summary>The cloud folder that contains <paramref name="path"/>, or null when it is not inside one.</summary>
    public CloudSyncRoot? FindContaining(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (string root in _roots)
        {
            if (!path.StartsWith(root, _comparison))
            {
                continue;
            }

            // "C:\Users\me\OneDrive-old" starts with "C:\Users\me\OneDrive" but is a different folder, so the
            // match has to end at the root's own boundary.
            if (path.Length == root.Length || IsSeparator(path[root.Length]))
            {
                return _byRoot[root];
            }
        }

        return null;
    }

    public bool IsInsideCloudDrive(string path) => FindContaining(path) is not null;

    private static bool IsSeparator(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    private static string TrimSeparator(string path)
    {
        // A drive root such as "C:\" keeps its separator; anything else loses a trailing one.
        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }
}
