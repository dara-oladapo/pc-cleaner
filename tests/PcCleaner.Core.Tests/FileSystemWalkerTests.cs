using PcCleaner.Core.Utilities;

namespace PcCleaner.Core.Tests;

public class FileSystemWalkerTests
{
    [Fact]
    public async Task EnumerateFilesAsync_ReportsProgressAndCompletes()
    {
        using var dir = new TestDirectory();
        for (int i = 0; i < 20; i++)
        {
            dir.WriteFileOfSize($"file{i}.bin", 10);
        }

        var reportedCounts = new List<int>();
        var progress = new Progress<int>(reportedCounts.Add);

        int seen = 0;
        await foreach (var _ in FileSystemWalker.EnumerateFilesAsync(dir.Path, filesVisited: progress))
        {
            seen++;
        }

        Assert.Equal(20, seen);

        // Progress<T> marshals via SynchronizationContext.Post, which may not have flushed yet on this thread —
        // give it a moment rather than asserting immediately after the enumeration completes.
        for (int i = 0; i < 20 && reportedCounts.Count == 0; i++)
        {
            await Task.Delay(10);
        }

        Assert.NotEmpty(reportedCounts);
        Assert.Equal(20, reportedCounts[^1]);
    }

    [Fact]
    public async Task EnumerateFilesAsync_RespectsCancellation()
    {
        using var dir = new TestDirectory();
        for (int i = 0; i < 50; i++)
        {
            dir.WriteFileOfSize($"file{i}.bin", 10);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in FileSystemWalker.EnumerateFilesAsync(dir.Path, cts.Token))
            {
            }
        });
    }
}
