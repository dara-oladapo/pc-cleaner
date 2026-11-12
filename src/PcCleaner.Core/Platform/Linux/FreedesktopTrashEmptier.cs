using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Platform.Linux;

/// <summary>
/// Empties a freedesktop.org trash directory (<c>$XDG_DATA_HOME/Trash</c>) the way the Trash spec lays it out,
/// rather than as an anonymous cache folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>files/</c> and <c>info/</c> themselves are never removed, only what is in them: file managers expect both
/// to exist. Each trashed entry <c>files/N</c> goes together with its <c>info/N.trashinfo</c>: the entry is
/// deleted first, and the <c>.trashinfo</c> only once the entry is completely gone. If part of an entry can't be
/// deleted, its <c>.trashinfo</c> stays, so what's left is still listed (and restorable) in the trash rather than
/// stranded as an invisible orphan. Leftover <c>.trashinfo</c> files with no entry, and the
/// <c>directorysizes</c> cache, are removed: both only describe entries that no longer exist.
/// </para>
/// <para>
/// Symlinks inside the trash are removed as links; their targets are never touched.
/// </para>
/// </remarks>
public static class FreedesktopTrashEmptier
{
    public const string FilesDirectory = "files";
    public const string InfoDirectory = "info";
    public const string InfoExtension = ".trashinfo";
    public const string DirectorySizesFile = "directorysizes";

    // TODO(#10 follow-up): the scanner refuses a Trash with another device mounted inside it, but that is checked
    // at scan time only. Re-check LinuxMountTable here (and in Core's JunkCleaner, which walks every other junk
    // root) immediately before deleting, so a mount added between scan and clean is never crossed.
    public static async Task<TrashResult> EmptyAsync(string trashRoot, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        string filesDir = Path.Combine(trashRoot, FilesDirectory);
        string infoDir = Path.Combine(trashRoot, InfoDirectory);

        foreach (string entry in SafeEnumerate(filesDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = await SizeOfAsync(entry, cancellationToken);
            if (!TryDeleteEntry(entry, out string? error))
            {
                failed.Add((entry, error!));
                continue;
            }

            succeeded.Add(entry);
            bytesFreed += size;

            string info = Path.Combine(infoDir, Path.GetFileName(entry) + InfoExtension);
            TryDeleteInfo(info, succeeded, failed, ref bytesFreed);
        }

        // Info files whose entry is gone (left by a crash mid-delete, or another tool) describe nothing.
        foreach (string info in SafeEnumerate(infoDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string name = Path.GetFileName(info);
            if (name.EndsWith(InfoExtension, StringComparison.Ordinal) &&
                !PathExists(Path.Combine(filesDir, name[..^InfoExtension.Length])))
            {
                TryDeleteInfo(info, succeeded, failed, ref bytesFreed);
            }
        }

        string sizes = Path.Combine(trashRoot, DirectorySizesFile);
        if (File.Exists(sizes))
        {
            TryDeleteInfo(sizes, succeeded, failed, ref bytesFreed);
        }

        return new TrashResult(succeeded, failed, bytesFreed);
    }

    private static IEnumerable<string> SafeEnumerate(string directory)
    {
        try
        {
            return Directory.Exists(directory) ? Directory.GetFileSystemEntries(directory) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool PathExists(string path) =>
        File.Exists(path) || Directory.Exists(path) || new FileInfo(path).LinkTarget is not null;

    private static async Task<long> SizeOfAsync(string entry, CancellationToken cancellationToken)
    {
        try
        {
            var info = new FileInfo(entry);
            if (info.LinkTarget is not null)
            {
                return 0;
            }

            if (info.Attributes.HasFlag(FileAttributes.Directory))
            {
                return (await FileSystemWalker.ComputeStatsAsync(entry, cancellationToken)).TotalBytes;
            }

            return info.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool TryDeleteEntry(string entry, out string? error)
    {
        try
        {
            var info = new FileInfo(entry);
            bool isDirectory = info.Attributes.HasFlag(FileAttributes.Directory);

            if (info.LinkTarget is not null)
            {
                // Remove the link itself. Windows needs the directory API for a directory link; Unix unlinks either.
                if (isDirectory && OperatingSystem.IsWindows())
                {
                    Directory.Delete(entry);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            else if (isDirectory)
            {
                // Recursive delete removes symlinks inside as links and does not descend through them.
                Directory.Delete(entry, recursive: true);
            }
            else
            {
                File.Delete(entry);
            }

            if (PathExists(entry))
            {
                error = "Could not be removed completely.";
                return false;
            }

            error = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return false;
        }
    }

    private static void TryDeleteInfo(string path, List<string> succeeded, List<(string Path, string Error)> failed, ref long bytesFreed)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                return;
            }

            long size = info.Length;
            info.Delete();
            succeeded.Add(path);
            bytesFreed += size;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add((path, ex.Message));
        }
    }
}
