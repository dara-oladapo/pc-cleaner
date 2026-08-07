using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class DuplicateFileScannerTests
{
    [Fact]
    public async Task ScanAsync_FindsFilesWithIdenticalContent()
    {
        using var dir = new TestDirectory();
        dir.WriteFile("a.txt", "duplicate content");
        dir.WriteFile("nested/b.txt", "duplicate content");
        dir.WriteFile("c.txt", "unique content");

        var scanner = new DuplicateFileScanner();
        var groups = await CollectAsync(scanner.ScanAsync([dir.Path]));

        var group = Assert.Single(groups);
        Assert.Equal(2, group.Files.Count);
        Assert.Equal(group.SizeBytesEach, group.ReclaimableBytes);
    }

    [Fact]
    public async Task ScanAsync_SameSizeDifferentContent_IsNotFlaggedAsDuplicate()
    {
        using var dir = new TestDirectory();
        dir.WriteFile("a.txt", "aaaaaaaaaa");
        dir.WriteFile("b.txt", "bbbbbbbbbb");

        var scanner = new DuplicateFileScanner();
        var groups = await CollectAsync(scanner.ScanAsync([dir.Path]));

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ScanAsync_RespectsMinimumFileSize()
    {
        using var dir = new TestDirectory();
        dir.WriteFile("a.txt", "hi");
        dir.WriteFile("b.txt", "hi");

        var scanner = new DuplicateFileScanner();
        var groups = await CollectAsync(scanner.ScanAsync([dir.Path], minFileSizeBytes: 1000));

        Assert.Empty(groups);
    }

    [Fact]
    public async Task ScanAsync_ThreeIdenticalFiles_GroupsAllThree()
    {
        using var dir = new TestDirectory();
        dir.WriteFile("a.txt", "same");
        dir.WriteFile("b.txt", "same");
        dir.WriteFile("c.txt", "same");

        var scanner = new DuplicateFileScanner();
        var groups = await CollectAsync(scanner.ScanAsync([dir.Path]));

        var group = Assert.Single(groups);
        Assert.Equal(3, group.Files.Count);
    }

    private static async Task<List<DuplicateGroup>> CollectAsync(IAsyncEnumerable<DuplicateGroup> source)
    {
        var list = new List<DuplicateGroup>();
        await foreach (var item in source)
        {
            list.Add(item);
        }

        return list;
    }
}
