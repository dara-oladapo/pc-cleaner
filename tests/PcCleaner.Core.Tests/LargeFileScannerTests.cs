using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class LargeFileScannerTests
{
    [Fact]
    public async Task ScanAsync_OnlyReturnsFilesAtOrAboveThreshold()
    {
        using var dir = new TestDirectory();
        dir.WriteFileOfSize("small.bin", 100);
        dir.WriteFileOfSize("big.bin", 10_000);
        dir.WriteFileOfSize("nested/also-big.bin", 20_000);

        var scanner = new LargeFileScanner();
        var results = await CollectAsync(scanner.ScanAsync([dir.Path], minSizeBytes: 5_000));

        Assert.Equal(2, results.Count);
        Assert.Contains(results, f => f.Path.EndsWith("big.bin"));
        Assert.Contains(results, f => f.Path.EndsWith("also-big.bin"));
        Assert.DoesNotContain(results, f => f.Path.EndsWith("small.bin"));
    }

    [Fact]
    public async Task ScanAsync_NonExistentRoot_YieldsNothing()
    {
        var scanner = new LargeFileScanner();
        var results = await CollectAsync(scanner.ScanAsync([Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())], minSizeBytes: 0));

        Assert.Empty(results);
    }

    [Fact]
    public async Task ScanAsync_MultipleRoots_CombinesResults()
    {
        using var dirA = new TestDirectory();
        using var dirB = new TestDirectory();
        dirA.WriteFileOfSize("a.bin", 1000);
        dirB.WriteFileOfSize("b.bin", 1000);

        var scanner = new LargeFileScanner();
        var results = await CollectAsync(scanner.ScanAsync([dirA.Path, dirB.Path], minSizeBytes: 1));

        Assert.Equal(2, results.Count);
    }

    private static async Task<List<FileEntry>> CollectAsync(IAsyncEnumerable<FileEntry> source)
    {
        var list = new List<FileEntry>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
