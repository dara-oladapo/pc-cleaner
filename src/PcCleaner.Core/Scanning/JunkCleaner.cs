using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;
using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Scanning;

/// <summary>Deletes junk contents directly (no trash round-trip) but leaves top-level cache/temp directories in place, since apps often assume they still exist.</summary>
public sealed class JunkCleaner : IJunkCleaner
{
    public async Task<TrashResult> CleanAsync(IReadOnlyList<JunkItem> items, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        foreach (JunkItem item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (item.IsDirectory)
            {
                await CleanDirectoryContentsAsync(item.Path, succeeded, failed, cancellationToken, sizeFreed => bytesFreed += sizeFreed);
            }
            else
            {
                TryDeleteFile(item.Path, succeeded, failed, sizeFreed => bytesFreed += sizeFreed);
            }
        }

        return new TrashResult(succeeded, failed, bytesFreed);
    }

    private static async Task CleanDirectoryContentsAsync(
        string directory,
        List<string> succeeded,
        List<(string Path, string Error)> failed,
        CancellationToken cancellationToken,
        Action<long> onBytesFreed)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        await foreach (var entry in FileSystemWalker.EnumerateFilesAsync(directory, cancellationToken))
        {
            TryDeleteFile(entry.Path, succeeded, failed, onBytesFreed);
        }

        RemoveEmptySubdirectories(directory);
    }

    private static void RemoveEmptySubdirectories(string root)
    {
        foreach (string dir in Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                     .OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
            catch (IOException)
            {
                // Left in place; not worth surfacing as a failure for a best-effort cleanup step.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void TryDeleteFile(string path, List<string> succeeded, List<(string, string)> failed, Action<long> onBytesFreed)
    {
        try
        {
            var info = new FileInfo(path);
            long size = info.Exists ? info.Length : 0;
            info.Delete();
            succeeded.Add(path);
            onBytesFreed(size);
        }
        catch (IOException ex)
        {
            failed.Add((path, ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            failed.Add((path, ex.Message));
        }
    }
}
