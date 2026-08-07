using System.Diagnostics;
using System.Threading.Channels;
using PcCleaner.Core.Models;

namespace PcCleaner.Core.Utilities;

/// <summary>Pure System.IO recursive walk shared by scanners and junk-size calculations. Skips reparse points to avoid symlink cycles and swallows per-entry access errors.</summary>
public static class FileSystemWalker
{
    // Directory enumeration is fully synchronous/blocking I/O (there's no true async variant), so it runs on a
    // dedicated background thread via Task.Run and hands results to the caller through a Channel. This avoids
    // running blocking I/O on the caller's thread (typically the UI thread) via cooperative Task.Yield() calls,
    // which — depending on the ambient SynchronizationContext — can lose the resumption continuation entirely
    // and hang with 0% CPU. A Channel gives proper backpressure with no such dependency.
    private const int MinProgressIntervalMs = 120;

    public static IAsyncEnumerable<FileEntry> EnumerateFilesAsync(
        string root,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        var channel = Channel.CreateBounded<FileEntry>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = true,
        });

        _ = Task.Run(async () =>
        {
            Exception? error = null;
            try
            {
                await WalkAsync(root, channel.Writer, cancellationToken, filesVisited).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the caller cancels; the channel just completes with no error below.
            }
            catch (Exception ex)
            {
                error = ex;
            }
            finally
            {
                channel.Writer.TryComplete(error);
            }
        }, CancellationToken.None);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    public static async Task<(long TotalBytes, int FileCount)> ComputeStatsAsync(
        string root,
        CancellationToken cancellationToken = default,
        IProgress<int>? filesVisited = null)
    {
        long totalBytes = 0;
        int fileCount = 0;

        await foreach (FileEntry entry in EnumerateFilesAsync(root, cancellationToken, filesVisited))
        {
            totalBytes += entry.SizeBytes;
            fileCount++;
        }

        return (totalBytes, fileCount);
    }

    private static async Task WalkAsync(
        string root,
        ChannelWriter<FileEntry> writer,
        CancellationToken cancellationToken,
        IProgress<int>? filesVisited)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        var pending = new Stack<string>();
        pending.Push(root);
        int visited = 0;
        Stopwatch? progressClock = filesVisited is null ? null : Stopwatch.StartNew();

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string dir = pending.Pop();

            string[] subDirs;
            string[] files;
            try
            {
                subDirs = Directory.GetDirectories(dir);
                files = Directory.GetFiles(dir);
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (string subDir in subDirs)
            {
                if (!IsReparsePoint(subDir))
                {
                    pending.Push(subDir);
                }
            }

            foreach (string file in files)
            {
                FileEntry? entry = TryDescribeFile(file);
                if (entry is not null)
                {
                    await writer.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
                }

                visited++;

                if (progressClock is not null && progressClock.ElapsedMilliseconds >= MinProgressIntervalMs)
                {
                    filesVisited!.Report(visited);
                    progressClock.Restart();
                }
            }
        }

        filesVisited?.Report(visited);
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return new DirectoryInfo(path).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static FileEntry? TryDescribeFile(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return null;
            }

            return new FileEntry(path, info.Length, info.LastWriteTimeUtc);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
