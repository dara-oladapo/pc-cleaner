using PcCleaner.Core.Models;
using PcCleaner.Core.Platform.Linux;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests.Linux;

public class FreedesktopTrashEmptierTests
{
    private static string Trash(TestDirectory dir) => Path.Combine(dir.Path, "Trash");

    private static void Trashed(TestDirectory dir, string name, int size, bool asDirectory = false)
    {
        if (asDirectory)
        {
            dir.WriteFileOfSize($"Trash/files/{name}/inner/a.bin", size);
        }
        else
        {
            dir.WriteFileOfSize($"Trash/files/{name}", size);
        }

        dir.WriteFile($"Trash/info/{name}.trashinfo", $"[Trash Info]\nPath=/home/ada/{name}\nDeletionDate=2026-01-01T00:00:00\n");
    }

    [Fact]
    public async Task EmptyAsync_RemovesEntriesAndInfo_ButKeepsFilesAndInfoDirectories()
    {
        using var dir = new TestDirectory();
        Trashed(dir, "report.pdf", 1000);
        Trashed(dir, "old-project", 2000, asDirectory: true);
        dir.WriteFile("Trash/info/ghost.trashinfo");          // entry already gone
        dir.WriteFile("Trash/directorysizes", "2000 0 old-project\n");

        var result = await FreedesktopTrashEmptier.EmptyAsync(Trash(dir));

        Assert.True(result.AllSucceeded);
        Assert.True(Directory.Exists(Path.Combine(Trash(dir), "files")));
        Assert.True(Directory.Exists(Path.Combine(Trash(dir), "info")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Trash(dir), "files")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Trash(dir), "info")));
        Assert.False(File.Exists(Path.Combine(Trash(dir), "directorysizes")));
        Assert.True(result.BytesFreed >= 3000);
    }

    [Fact]
    public async Task EmptyAsync_KeepsTheTrashInfo_WhenItsEntryCannotBeDeleted()
    {
        using var dir = new TestDirectory();
        Trashed(dir, "stuck", 100, asDirectory: true);
        Trashed(dir, "fine.txt", 50);
        string stuckDir = Path.Combine(Trash(dir), "files", "stuck", "inner");

        using IDisposable blocker = BlockDeletionInside(stuckDir);

        var result = await FreedesktopTrashEmptier.EmptyAsync(Trash(dir));

        Assert.Contains(result.FailedPaths, f => f.Path.EndsWith("stuck", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(Trash(dir), "info", "stuck.trashinfo")));   // still listed in the trash
        Assert.False(File.Exists(Path.Combine(Trash(dir), "files", "fine.txt")));
        Assert.False(File.Exists(Path.Combine(Trash(dir), "info", "fine.txt.trashinfo"))); // went with its entry
    }

    [Fact]
    public async Task EmptyAsync_RemovesSymlinksAsLinks_NeverTheirTargets()
    {
        using var dir = new TestDirectory();
        string outside = dir.WriteFileOfSize("outside/real-data.bin", 500);
        Directory.CreateDirectory(Path.Combine(Trash(dir), "files"));
        string link = Path.Combine(Trash(dir), "files", "link-to-data");
        string dirLink = Path.Combine(Trash(dir), "files", "link-to-dir");
        try
        {
            File.CreateSymbolicLink(link, outside);
            Directory.CreateSymbolicLink(dirLink, Path.GetDirectoryName(outside)!);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Windows without Developer Mode can't create symlinks; Linux CI covers this.
        }

        dir.WriteFile("Trash/info/link-to-data.trashinfo");
        dir.WriteFile("Trash/info/link-to-dir.trashinfo");

        var result = await FreedesktopTrashEmptier.EmptyAsync(Trash(dir));

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(outside));
        Assert.Equal(500, new FileInfo(outside).Length);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(Trash(dir), "files")));
    }

    [Fact]
    public async Task LinuxJunkCleaner_EmptiesTrashBySpec_AndHandsEverythingElseToTheSharedCleaner()
    {
        using var dir = new TestDirectory();
        Trashed(dir, "a.txt", 10);
        dir.WriteFileOfSize("cache/pip/x.bin", 20);

        var cleaner = new LinuxJunkCleaner(new JunkCleaner());
        var result = await cleaner.CleanAsync(
        [
            new JunkItem(Trash(dir), 10, JunkCategory.TrashBin, "Trash", IsDirectory: true),
            new JunkItem(Path.Combine(dir.Path, "cache", "pip"), 20, JunkCategory.PackageManagerCache, "pip", IsDirectory: true),
        ]);

        Assert.True(result.AllSucceeded);
        Assert.True(Directory.Exists(Path.Combine(Trash(dir), "files")));
        Assert.True(Directory.Exists(Path.Combine(Trash(dir), "info")));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(dir.Path, "cache", "pip")));
        Assert.True(result.BytesFreed >= 30);
    }

    /// <summary>Makes deleting anything in <paramref name="directory"/> fail until disposed.</summary>
    private static IDisposable BlockDeletionInside(string directory)
    {
        if (OperatingSystem.IsWindows())
        {
            // An open handle without FILE_SHARE_DELETE blocks deleting the file, and so its directory.
            return new FileStream(Path.Combine(directory, "a.bin"), FileMode.Open, FileAccess.Read, FileShare.None);
        }

        // Unix ignores open handles; a read-only directory stops its entries being unlinked.
        UnixFileMode original = File.GetUnixFileMode(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restore(() => File.SetUnixFileMode(directory, original));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
