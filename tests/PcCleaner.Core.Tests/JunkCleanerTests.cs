using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class JunkCleanerTests
{
    private static readonly DateTime FutureNow = DateTime.UtcNow.AddDays(10);

    /// <summary>A clock fixed at <see cref="FutureNow"/>, so files the test just created count as old.</summary>
    private static JunkCleaner FutureCleaner() => new(new FixedTime(FutureNow));

    private sealed class FixedTime(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }

    /// <summary>Builds a directory item the way the real scanner does, from a walk of the folder.</summary>
    private static JunkItem ScanAsItem(string path, bool isTemp = false)
    {
        var root = new PlannedJunkRoot(JunkRootPlanner.Normalize(path), JunkCategory.SystemCache, "Cache", isTemp, []);
        JunkRootScan scan = JunkRootScanner.Scan(root, FutureNow, JunkSafetyRules.DefaultMinimumAge, JunkRootPlanner.DefaultPathComparison);
        return new JunkItem(root.Path, scan.TotalBytes, JunkCategory.SystemCache, "Cache", IsDirectory: true)
        {
            Files = scan.Files,
            Directories = scan.Directories,
            MinimumAge = isTemp ? JunkSafetyRules.DefaultMinimumAge : null,
        };
    }

    private static JunkFileEntry EntryFor(string path) =>
        new(path, new FileInfo(path).Length, File.GetLastWriteTimeUtc(path), File.GetCreationTimeUtc(path));

    [Fact]
    public async Task CleanAsync_DirectoryItem_DeletesContentsButKeepsTopLevelFolder()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        dir.WriteFileOfSize("cache/a.bin", 100);
        dir.WriteFileOfSize("cache/sub/b.bin", 200);

        var result = await new JunkCleaner().CleanAsync([ScanAsItem(cacheDir)]);

        Assert.True(result.AllSucceeded);
        Assert.Empty(result.SkippedPaths);
        Assert.Equal(300, result.BytesFreed);
        Assert.True(Directory.Exists(cacheDir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(cacheDir));
    }

    [Fact]
    public async Task CleanAsync_DeletesOnlyTheScannedSet_NotFilesThatAppearedLater()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        dir.WriteFileOfSize("cache/scanned.bin", 10);
        JunkItem item = ScanAsItem(cacheDir);

        string later = dir.WriteFileOfSize("cache/sub/later.bin", 10);

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(later));
        Assert.Equal(10, result.BytesFreed);
        Assert.Single(result.SucceededPaths);
    }

    [Fact]
    public async Task CleanAsync_TempItem_LeavesRecentFilesAndFolders()
    {
        using var dir = new TestDirectory();
        string temp = Path.Combine(dir.Path, "Temp");
        dir.WriteFileOfSize("Temp/old.tmp", 10);
        string recent = dir.WriteFileOfSize("Temp/recent.tmp", 10);
        string claude = dir.WriteFileOfSize("Temp/claude/scratch.txt", 10);
        string disk = dir.WriteFileOfSize("Temp/old/ext4.vhdx", 10);
        File.SetLastWriteTimeUtc(recent, FutureNow.AddHours(-1));

        var result = await FutureCleaner().CleanAsync([ScanAsItem(temp, isTemp: true)]);

        Assert.False(File.Exists(Path.Combine(temp, "old.tmp")));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(claude));
        Assert.True(File.Exists(disk));
        Assert.Equal(10, result.BytesFreed);
    }

    [Fact]
    public async Task CleanAsync_FileChangedSinceScan_IsSkipped()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string file = dir.WriteFileOfSize("cache/a.bin", 10);
        JunkItem item = ScanAsItem(cacheDir);

        File.SetLastWriteTimeUtc(file, File.GetLastWriteTimeUtc(file).AddMinutes(5));

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(file));
        Assert.True(result.AllSucceeded);
        Assert.Single(result.SkippedPaths);
    }

    [Fact]
    public async Task CleanAsync_LockedFile_IsSkippedNotFailed_AndOthersStillDeleted()
    {
        // Only Windows refuses to delete an open file; elsewhere the delete just succeeds.
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string locked = dir.WriteFileOfSize("cache/locked.bin", 10);
        string free = dir.WriteFileOfSize("cache/free.bin", 10);
        JunkItem item = ScanAsItem(cacheDir);

        TrashResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await new JunkCleaner().CleanAsync([item]);
        }

        Assert.True(result.AllSucceeded);
        Assert.Single(result.SkippedPaths);
        Assert.Equal(10, result.BytesFreed);
        Assert.True(File.Exists(locked));
        Assert.False(File.Exists(free));
    }

    [Fact]
    public async Task CleanAsync_DoesNotTouchAnythingReachedThroughALink()
    {
        // Regression: removing empty sub-folders used to recurse through junctions and delete empty folders
        // outside the scanned root. A manifest that names paths through a link must be refused too.
        using var dir = new TestDirectory();
        string root = Path.Combine(dir.Path, "root");
        string outside = Path.Combine(dir.Path, "outside");
        dir.WriteFileOfSize("root/junk.bin", 1);
        string precious = dir.WriteFileOfSize("outside/precious.dat", 5);
        string outsideEmpty = Path.Combine(outside, "empty");
        Directory.CreateDirectory(outsideEmpty);

        string link = Path.Combine(root, "link");
        if (!TestLinks.TryCreateDirectoryLink(link, outside))
        {
            return;
        }

        JunkItem scanned = ScanAsItem(root);
        var tampered = scanned with
        {
            Files = [.. scanned.Files!, EntryFor(precious) with { Path = Path.Combine(link, "precious.dat") }],
            Directories =
            [
                .. scanned.Directories!,
                new JunkDirectoryEntry(Path.Combine(link, "empty"), Directory.GetCreationTimeUtc(outsideEmpty)),
                new JunkDirectoryEntry(link, Directory.GetCreationTimeUtc(outside)),
            ],
        };

        var result = await new JunkCleaner().CleanAsync([tampered]);

        Assert.True(File.Exists(precious));
        Assert.True(Directory.Exists(outsideEmpty));
        Assert.True(Directory.Exists(link));
        Assert.False(File.Exists(Path.Combine(root, "junk.bin")));
        Assert.Equal(1, result.BytesFreed);
    }

    [Fact]
    public async Task CleanAsync_ManifestEntryOutsideRoot_IsSkipped()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        Directory.CreateDirectory(cacheDir);
        string elsewhere = dir.WriteFileOfSize("elsewhere/keep.bin", 10);

        var item = new JunkItem(cacheDir, 10, JunkCategory.SystemCache, "Cache", IsDirectory: true)
        {
            Files = [EntryFor(elsewhere) with { Path = Path.Combine(cacheDir, "..", "elsewhere", "keep.bin") }],
            Directories = [],
        };

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(elsewhere));
        Assert.Single(result.SkippedPaths);
    }

    [Fact]
    public async Task CleanAsync_DirectoryItemWithoutManifest_IsRefused()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string file = dir.WriteFileOfSize("cache/a.bin", 10);

        var item = new JunkItem(cacheDir, 10, JunkCategory.SystemCache, "Cache", IsDirectory: true);
        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(file));
        Assert.Single(result.SkippedPaths);
        Assert.Equal(0, result.BytesFreed);
    }

    [Fact]
    public async Task CleanAsync_SizeChangedButWriteTimeRestored_IsSkipped()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string file = dir.WriteFileOfSize("cache/a.bin", 10);
        JunkItem item = ScanAsItem(cacheDir);

        DateTime written = File.GetLastWriteTimeUtc(file);
        File.WriteAllBytes(file, new byte[20]);
        File.SetLastWriteTimeUtc(file, written);

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(file));
        Assert.Single(result.SkippedPaths);
    }

    [Fact]
    public async Task CleanAsync_TempItem_ReappliesAgeWithCurrentClock()
    {
        // Scanned with a clock 10 days ahead (so the file was "old"), cleaned with the real clock: the file is
        // only seconds old right now and must survive.
        using var dir = new TestDirectory();
        string temp = Path.Combine(dir.Path, "Temp");
        string file = dir.WriteFileOfSize("Temp/a.tmp", 10);
        JunkItem item = ScanAsItem(temp, isTemp: true);
        Assert.Single(item.Files!);

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(file));
        Assert.Single(result.SkippedPaths);
        Assert.Equal(0, result.BytesFreed);
    }

    [Fact]
    public async Task CleanAsync_TempItem_EmptyFolderRemovedOnlyWhenOldByCurrentClock()
    {
        using var dir = new TestDirectory();
        string temp = Path.Combine(dir.Path, "Temp");
        string folder = Path.Combine(temp, "empty");
        Directory.CreateDirectory(folder);
        dir.WriteFileOfSize("Temp/keep-root-nonempty.tmp", 1);
        JunkItem item = ScanAsItem(temp, isTemp: true);
        Assert.Single(item.Directories!);

        await new JunkCleaner().CleanAsync([item]);
        Assert.True(Directory.Exists(folder));

        await FutureCleaner().CleanAsync([item]);
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task CleanAsync_RecreatedFolder_IsNotRemoved()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string folder = Path.Combine(cacheDir, "sub");
        Directory.CreateDirectory(folder);
        JunkItem scanned = ScanAsItem(cacheDir);

        var item = scanned with
        {
            Directories = [new JunkDirectoryEntry(folder, Directory.GetCreationTimeUtc(folder).AddDays(-1))],
        };

        await new JunkCleaner().CleanAsync([item]);

        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public async Task CleanAsync_NonCanonicalManifestPath_IsSkippedEvenInsideRoot()
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string file = dir.WriteFileOfSize("cache/a.bin", 10);
        Directory.CreateDirectory(Path.Combine(cacheDir, "sub"));
        string sep = Path.DirectorySeparatorChar.ToString();

        var item = new JunkItem(JunkRootPlanner.Normalize(cacheDir), 10, JunkCategory.SystemCache, "Cache", IsDirectory: true)
        {
            Files = [EntryFor(file) with { Path = cacheDir + sep + "sub" + sep + ".." + sep + "a.bin" }],
            Directories = [],
        };

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(File.Exists(file));
        Assert.Single(result.SkippedPaths);
    }

    [Theory]
    [InlineData("vm.vmdk")]
    [InlineData("vm.vdi")]
    [InlineData("vm.qcow2")]
    [InlineData("install.iso")]
    [InlineData("boot.wim")]
    public async Task CleanAsync_ProtectedDiskImage_NeverDeleted(string name)
    {
        using var dir = new TestDirectory();
        string cacheDir = Path.Combine(dir.Path, "cache");
        string file = dir.WriteFileOfSize("cache/" + name, 10);

        JunkItem scanned = ScanAsItem(cacheDir);
        Assert.Empty(scanned.Files!);

        var forced = scanned with { Files = [EntryFor(file)] };
        var result = await new JunkCleaner().CleanAsync([forced]);

        Assert.True(File.Exists(file));
        Assert.Single(result.SkippedPaths);
    }

    [Fact]
    public async Task CleanAsync_FileItem_DeletesSingleFile()
    {
        using var dir = new TestDirectory();
        string filePath = dir.WriteFileOfSize("log.txt", 50);

        var item = new JunkItem(filePath, SizeBytes: 50, JunkCategory.SystemLogs, "Log", IsDirectory: false);

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(50, result.BytesFreed);
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task CleanAsync_NonExistentDirectory_SucceedsWithNothingToDo()
    {
        using var dir = new TestDirectory();
        var item = new JunkItem(
            Path.Combine(dir.Path, "missing"),
            SizeBytes: 0,
            JunkCategory.TempFiles,
            "Missing",
            IsDirectory: true);

        var result = await new JunkCleaner().CleanAsync([item]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(0, result.BytesFreed);
    }
}
