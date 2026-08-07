using Foundation;
using PcCleaner.Core.Abstractions;
using PcCleaner.Core.Models;

namespace PcCleaner.App.Platforms.MacCatalyst;

/// <summary>Moves files/folders to macOS Trash via NSFileManager.TrashItem (reversible, unlike File.Delete).</summary>
public sealed class MacFileTrasher : IFileTrasher
{
    public Task<TrashResult> MoveToTrashAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        var succeeded = new List<string>();
        var failed = new List<(string Path, string Error)>();
        long bytesFreed = 0;

        NSFileManager fileManager = NSFileManager.DefaultManager;

        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            long size = GetSize(path);
            using NSUrl url = NSUrl.FromFilename(path);

            bool ok = fileManager.TrashItem(url, out _, out NSError? error);
            if (ok)
            {
                succeeded.Add(path);
                bytesFreed += size;
            }
            else
            {
                failed.Add((path, error?.LocalizedDescription ?? "Unknown error"));
            }
        }

        return Task.FromResult(new TrashResult(succeeded, failed, bytesFreed));
    }

    private static long GetSize(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                return Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                    .Sum(TryGetLength);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return 0;
    }

    private static long TryGetLength(string file)
    {
        try
        {
            return new FileInfo(file).Length;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
