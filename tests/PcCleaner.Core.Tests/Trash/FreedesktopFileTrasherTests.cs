using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

/// <summary>
/// The whole trash algorithm against <see cref="FakeTrashPlatform"/>: real files in a temp directory, simulated
/// devices. Runs on every OS. Layout:
/// <code>
///   {temp}/home        "device" 8:1, mount 10 — holds the home trash at home/data/Trash
///   {temp}/usb drive   "device" 8:17, mount 20 — a second drive (with a space, to exercise escaping)
/// </code>
/// </summary>
public sealed class FreedesktopFileTrasherTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 11, 12, 9, 30, 15);

    private readonly TestDirectory _dir = new();
    private readonly FakeTrashPlatform _platform = new();
    private readonly string _home;
    private readonly string _homeTrash;
    private readonly string _usb;

    public FreedesktopFileTrasherTests()
    {
        _home = Path.Combine(_dir.Path, "home");
        _usb = Path.Combine(_dir.Path, "usb drive");
        Directory.CreateDirectory(_home);
        Directory.CreateDirectory(_usb);
        _homeTrash = Path.Combine(_home, "data", "Trash");

        _platform.AddMount(10, _home, 8, 1);
        _platform.AddMount(20, _usb, 8, 17);
    }

    public void Dispose() => _dir.Dispose();

    private FreedesktopFileTrasher CreateTrasher() => new(_platform, _homeTrash, () => Now);

    /// <summary>The expected info file. Absolute paths are normalised the way the trasher sees them (on Windows,
    /// TestDirectory hands back paths with '/' in them).</summary>
    private static string Info(string path) =>
        TrashInfoFile.Format(Path.IsPathRooted(path) ? Path.GetFullPath(path) : path, Now);

    [Fact]
    public async Task HomeDevice_MovesIntoHomeTrash_WithAbsolutePath()
    {
        string file = _dir.WriteFileOfSize("home/docs/report.pdf", 1234);

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.Equal([file], result.SucceededPaths);
        Assert.Equal(1234, result.BytesFreed);
        Assert.False(File.Exists(file));
        Assert.Equal(1234, new FileInfo(Path.Combine(_homeTrash, "files", "report.pdf")).Length);
        Assert.Equal(Info(file), File.ReadAllText(Path.Combine(_homeTrash, "info", "report.pdf.trashinfo")));
    }

    [Fact]
    public async Task HomeTrash_CreatedWithMissingParents()
    {
        string file = _dir.WriteFile("home/a.txt");

        await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(Directory.Exists(Path.Combine(_homeTrash, "files")));
        Assert.True(Directory.Exists(Path.Combine(_homeTrash, "info")));
        Assert.Contains("mkdir " + Path.Combine(_home, "data"), _platform.Log);
    }

    [Fact]
    public async Task InfoFileIsWrittenBeforeTheMove()
    {
        string file = _dir.WriteFile("home/a.txt", "payload");
        string? infoAtRename = null;
        bool fileStillInPlaceAtRename = false;
        _platform.BeforeRename = (_, _) =>
        {
            string info = Path.Combine(_homeTrash, "info", "a.txt.trashinfo");
            infoAtRename = File.Exists(info) ? File.ReadAllText(info) : null;
            fileStillInPlaceAtRename = File.Exists(file);
        };

        await CreateTrasher().MoveToTrashAsync([file]);

        Assert.Equal(Info(file), infoAtRename);
        Assert.True(fileStillInPlaceAtRename);
    }

    [Fact]
    public async Task NameCollision_DeduplicatesAndKeepsBoth()
    {
        string first = _dir.WriteFile("home/one/notes.txt", "first");
        string second = _dir.WriteFile("home/two/notes.txt", "second");

        var result = await CreateTrasher().MoveToTrashAsync([first, second]);

        Assert.True(result.AllSucceeded);
        Assert.Equal("first", File.ReadAllText(Path.Combine(_homeTrash, "files", "notes.txt")));
        Assert.Equal("second", File.ReadAllText(Path.Combine(_homeTrash, "files", "notes.2.txt")));
        Assert.Equal(Info(first), File.ReadAllText(Path.Combine(_homeTrash, "info", "notes.txt.trashinfo")));
        Assert.Equal(Info(second), File.ReadAllText(Path.Combine(_homeTrash, "info", "notes.2.txt.trashinfo")));
    }

    [Fact]
    public async Task OrphanedInfoFile_NameIsSkippedAndOrphanUntouched()
    {
        Directory.CreateDirectory(Path.Combine(_homeTrash, "info"));
        Directory.CreateDirectory(Path.Combine(_homeTrash, "files"));
        string orphan = Path.Combine(_homeTrash, "info", "a.txt.trashinfo");
        File.WriteAllText(orphan, "left by a crash");
        string file = _dir.WriteFile("home/a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.Equal("left by a crash", File.ReadAllText(orphan));
        Assert.True(File.Exists(Path.Combine(_homeTrash, "files", "a.2.txt")));
    }

    [Fact]
    public async Task ItemInFilesWithoutInfo_NameIsSkippedAndReservationReleased()
    {
        Directory.CreateDirectory(Path.Combine(_homeTrash, "info"));
        Directory.CreateDirectory(Path.Combine(_homeTrash, "files"));
        File.WriteAllText(Path.Combine(_homeTrash, "files", "a.txt"), "someone else's");
        string file = _dir.WriteFile("home/a.txt", "mine");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.Equal("someone else's", File.ReadAllText(Path.Combine(_homeTrash, "files", "a.txt")));
        Assert.False(File.Exists(Path.Combine(_homeTrash, "info", "a.txt.trashinfo")));
        Assert.Equal("mine", File.ReadAllText(Path.Combine(_homeTrash, "files", "a.2.txt")));
    }

    [Fact]
    public async Task Directory_IsMovedWhole_AndSized()
    {
        _dir.WriteFileOfSize("home/album/1.jpg", 100);
        _dir.WriteFileOfSize("home/album/sub/2.jpg", 250);
        string album = Path.Combine(_home, "album");

        var result = await CreateTrasher().MoveToTrashAsync([album]);

        Assert.True(result.AllSucceeded);
        Assert.Equal(350, result.BytesFreed);
        Assert.False(Directory.Exists(album));
        Assert.True(File.Exists(Path.Combine(_homeTrash, "files", "album", "sub", "2.jpg")));
        Assert.Equal(Info(album), File.ReadAllText(Path.Combine(_homeTrash, "info", "album.trashinfo")));
    }

    [Fact]
    public async Task TrailingSeparator_IsTolerated()
    {
        _dir.WriteFile("home/folder/x.txt");
        string folder = Path.Combine(_home, "folder");

        var result = await CreateTrasher().MoveToTrashAsync([folder + Path.DirectorySeparatorChar]);

        Assert.True(result.AllSucceeded);
        Assert.True(Directory.Exists(Path.Combine(_homeTrash, "files", "folder")));
    }

    [Fact]
    public async Task OtherDevice_UsesPrivateTopdirTrash_WithRelativePath()
    {
        string file = _dir.WriteFileOfSize("usb drive/movies/big film.mkv", 5000);
        string trash = Path.Combine(_usb, ".Trash-1000");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded, string.Join("; ", result.FailedPaths));
        Assert.Equal(5000, result.BytesFreed);
        Assert.True(File.Exists(Path.Combine(trash, "files", "big film.mkv")));
        string relative = Path.GetRelativePath(_usb, Path.GetFullPath(file));
        Assert.Equal(Info(relative), File.ReadAllText(Path.Combine(trash, "info", "big film.mkv.trashinfo")));
        Assert.DoesNotContain(_usb, File.ReadAllText(Path.Combine(trash, "info", "big film.mkv.trashinfo")));
        Assert.False(Directory.Exists(_homeTrash)); // The home trash wasn't touched.
    }

    [Fact]
    public async Task OtherDevice_StickySharedTrash_UsesPerUserSubdirectory()
    {
        string shared = Path.Combine(_usb, ".Trash");
        Directory.CreateDirectory(shared);
        _platform.StickyDirectories.Add(shared);
        string file = _dir.WriteFile("usb drive/a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(Path.Combine(shared, "1000", "files", "a.txt")));
        Assert.Equal("[Trash Info]\nPath=a.txt\nDeletionDate=2026-11-12T09:30:15\n",
            File.ReadAllText(Path.Combine(shared, "1000", "info", "a.txt.trashinfo")));
        Assert.False(Directory.Exists(Path.Combine(_usb, ".Trash-1000")));
    }

    [Fact]
    public async Task OtherDevice_SharedTrashWithoutStickyBit_FallsBackToPrivateTrash()
    {
        Directory.CreateDirectory(Path.Combine(_usb, ".Trash"));
        string file = _dir.WriteFile("usb drive/a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(Path.Combine(_usb, ".Trash-1000", "files", "a.txt")));
        Assert.False(Directory.Exists(Path.Combine(_usb, ".Trash", "1000")));
    }

    [Fact]
    public async Task OtherDevice_SharedPerUserDirUncreatable_FallsBackToPrivateTrash()
    {
        string shared = Path.Combine(_usb, ".Trash");
        Directory.CreateDirectory(shared);
        _platform.StickyDirectories.Add(shared);
        _platform.UnwritableDirectories.Add(shared);
        string file = _dir.WriteFile("usb drive/a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(Path.Combine(_usb, ".Trash-1000", "files", "a.txt")));
    }

    [Fact]
    public async Task OtherDevice_NoUsableTrash_ReportsFailureAndLeavesFileAlone()
    {
        _platform.UnwritableDirectories.Add(_usb);
        string file = _dir.WriteFile("usb drive/keep.txt", "precious");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.False(result.AllSucceeded);
        var (path, error) = Assert.Single(result.FailedPaths);
        Assert.Equal(file, path);
        Assert.Contains("no usable Trash", error);
        Assert.Contains("never copies", error);
        Assert.Equal("precious", File.ReadAllText(file));
        Assert.Equal(0, result.BytesFreed);
        Assert.DoesNotContain(_platform.Log, l => l.StartsWith("rename", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OtherDevice_PrivateTrashOwnedBySomeoneElse_Refused()
    {
        string foreign = Path.Combine(_usb, ".Trash-1000");
        Directory.CreateDirectory(foreign);
        _platform.ForeignOwned.Add(foreign);
        string file = _dir.WriteFile("usb drive/a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.Contains("another user", Assert.Single(result.FailedPaths).Error);
        Assert.True(File.Exists(file));
        Assert.False(Directory.Exists(Path.Combine(foreign, "files")));
    }

    [Fact]
    public async Task ReadOnlyMount_ReportsFailureWithoutWriting()
    {
        var platform = new FakeTrashPlatform();
        platform.AddMount(10, _home, 8, 1);
        platform.AddMount(20, _usb, 8, 17, readOnly: true);
        string file = _dir.WriteFile("usb drive/a.txt");

        var result = await new FreedesktopFileTrasher(platform, _homeTrash, () => Now).MoveToTrashAsync([file]);

        Assert.Contains("read-only", Assert.Single(result.FailedPaths).Error);
        Assert.True(File.Exists(file));
        Assert.DoesNotContain(platform.Log, l => l.StartsWith("mkdir", StringComparison.Ordinal) || l.StartsWith("create", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MountInfoUnavailable_HomeTrashStillWorks_OtherDeviceFails()
    {
        _platform.MountInfoUnavailable = true;
        string onHome = _dir.WriteFile("home/a.txt");
        string onUsb = _dir.WriteFile("usb drive/b.txt");

        var result = await CreateTrasher().MoveToTrashAsync([onHome, onUsb]);

        Assert.Equal([onHome], result.SucceededPaths);
        Assert.Equal(onUsb, Assert.Single(result.FailedPaths).Path);
        Assert.True(File.Exists(onUsb));
    }

    [Fact]
    public async Task CrossDeviceRename_RemovesReservationAndReportsFailure()
    {
        string file = _dir.WriteFile("home/a.txt");
        _platform.ForceRenameOutcome = RenameOutcome.CrossDevice;

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.Contains("never copies", Assert.Single(result.FailedPaths).Error);
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_homeTrash, "info")));
    }

    [Fact]
    public async Task FailedRename_RemovesReservationAndReportsError()
    {
        string file = _dir.WriteFile("home/a.txt");
        _platform.ForceRenameOutcome = RenameOutcome.Failed;

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.Equal("Forced failure", Assert.Single(result.FailedPaths).Error);
        Assert.True(File.Exists(file));
        Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_homeTrash, "info")));
    }

    [Fact]
    public async Task LostRaceForName_TriesNextName()
    {
        string file = _dir.WriteFile("home/a.txt");
        _platform.ForceRenameOutcome = RenameOutcome.DestinationExists;

        var result = await CreateTrasher().MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(Path.Combine(_homeTrash, "files", "a.2.txt")));
        Assert.False(File.Exists(Path.Combine(_homeTrash, "info", "a.txt.trashinfo")));
        Assert.True(File.Exists(Path.Combine(_homeTrash, "info", "a.2.txt.trashinfo")));
    }

    [Fact]
    public async Task PartialFailure_DoesNotAbortTheBatch()
    {
        string a = _dir.WriteFileOfSize("home/a.bin", 10);
        string missing = Path.Combine(_home, "missing.bin");
        string b = _dir.WriteFileOfSize("usb drive/b.bin", 20);

        var result = await CreateTrasher().MoveToTrashAsync([a, missing, b]);

        Assert.Equal([a, b], result.SucceededPaths);
        Assert.Equal(missing, Assert.Single(result.FailedPaths).Path);
        Assert.Equal(30, result.BytesFreed);
    }

    [Fact]
    public async Task ItemAlreadyInTrash_Refused()
    {
        string file = _dir.WriteFile("home/a.txt");
        await CreateTrasher().MoveToTrashAsync([file]);
        string trashed = Path.Combine(_homeTrash, "files", "a.txt");

        var result = await CreateTrasher().MoveToTrashAsync([trashed]);

        Assert.Contains("already in the Trash", Assert.Single(result.FailedPaths).Error);
        Assert.True(File.Exists(trashed));
    }

    [Fact]
    public async Task AncestorOfTrash_Refused()
    {
        _dir.WriteFile("home/data/other.txt");
        string data = Path.Combine(_home, "data");

        var result = await CreateTrasher().MoveToTrashAsync([data]);

        Assert.Contains("contains the Trash", Assert.Single(result.FailedPaths).Error);
        Assert.True(Directory.Exists(data));
    }

    [Fact]
    public async Task MountPoint_Refused()
    {
        var result = await CreateTrasher().MoveToTrashAsync([_usb]);

        Assert.Contains("mount point", Assert.Single(result.FailedPaths).Error);
        Assert.True(Directory.Exists(_usb));
    }

    [Fact]
    public async Task NoHomeTrash_SameDeviceItemsUseTopdirTrash()
    {
        string file = _dir.WriteFile("home/a.txt");

        var result = await new FreedesktopFileTrasher(_platform, homeTrashDirectory: null, () => Now).MoveToTrashAsync([file]);

        Assert.True(result.AllSucceeded);
        Assert.True(File.Exists(Path.Combine(_home, ".Trash-1000", "files", "a.txt")));
    }

    [Fact]
    public async Task Cancellation_Throws()
    {
        string file = _dir.WriteFile("home/a.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateTrasher().MoveToTrashAsync([file], cts.Token));
        Assert.True(File.Exists(file));
    }
}
