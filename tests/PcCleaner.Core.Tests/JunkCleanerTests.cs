using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class JunkCleanerTests
{
    [Fact]
    public async Task CleanAsync_DirectoryItem_DeletesContentsButKeepsTopLevelFolder()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        dir.WriteFileOfSize("cache/a.bin", 100);
        dir.WriteFileOfSize("cache/sub/b.bin", 200);

        var item = new JunkItem(cacheDir, SizeBytes: 300, JunkCategory.SystemCache, "Cache", IsDirectory: true);

        var cleaner = new JunkCleaner();
        var result = await cleaner.CleanAsync([item]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(300, result.BytesFreed);
        Assert.True(Directory.Exists(cacheDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cacheDir));
    }

    [Fact]
    public async Task CleanAsync_FileItem_DeletesSingleFile()
    {
        using var dir = new TestDirectory();
        string filePath = dir.WriteFileOfSize("log.txt", 50);

        var item = new JunkItem(filePath, SizeBytes: 50, JunkCategory.SystemLogs, "Log", IsDirectory: false);

        var cleaner = new JunkCleaner();
        var result = await cleaner.CleanAsync([item]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(50, result.BytesFreed);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task CleanAsync_NonExistentDirectory_SucceedsWithNothingToDo()
    {
        var item = new JunkItem(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()),
            SizeBytes: 0,
            JunkCategory.TempFiles,
            "Missing",
            IsDirectory: true);

        var cleaner = new JunkCleaner();
        var result = await cleaner.CleanAsync([item]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(0, result.BytesFreed);
    }
}
