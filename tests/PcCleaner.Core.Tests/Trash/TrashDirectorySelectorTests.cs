using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

public class TrashDirectorySelectorTests
{
    private const uint Uid = 1000;
    private static readonly string Top = Path.Combine("/", "media", "usb");
    private static readonly string Shared = Path.Combine(Top, ".Trash");
    private static readonly string SharedUser = Path.Combine(Shared, "1000");
    private static readonly string Private = Path.Combine(Top, ".Trash-1000");

    private static FileSystemEntryStatus Dir(bool sticky = false, uint owner = Uid) =>
        new(8, 1, 5, IsDirectory: true, IsSymbolicLink: false, IsSticky: sticky, OwnerUid: owner, Size: 4096);

    private static readonly FileSystemEntryStatus Symlink =
        new(8, 1, 5, IsDirectory: false, IsSymbolicLink: true, IsSticky: false, OwnerUid: Uid, Size: 10);

    private static readonly FileSystemEntryStatus RegularFile =
        new(8, 1, 5, IsDirectory: false, IsSymbolicLink: false, IsSticky: false, OwnerUid: Uid, Size: 10);

    private static TrashDirectorySelection Select(Dictionary<string, FileSystemEntryStatus> fs) =>
        TrashDirectorySelector.Select(Top, Uid, p => fs.GetValueOrDefault(p));

    [Fact]
    public void NothingExists_CreatesPrivateTrash()
    {
        var result = Select([]);

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: true)], result.Candidates);
    }

    [Fact]
    public void StickySharedTrash_PreferredAndPerUserDirCreated_PrivateAsFallback()
    {
        var result = Select(new() { [Shared] = Dir(sticky: true, owner: 0) });

        Assert.Equal(
            [new TrashDirectoryCandidate(SharedUser, MustCreate: true), new TrashDirectoryCandidate(Private, MustCreate: true)],
            result.Candidates);
    }

    [Fact]
    public void StickySharedTrash_ExistingPerUserDirUsed()
    {
        var result = Select(new() { [Shared] = Dir(sticky: true, owner: 0), [SharedUser] = Dir() });

        Assert.Equal(new TrashDirectoryCandidate(SharedUser, MustCreate: false), result.Candidates[0]);
    }

    [Fact]
    public void SharedTrashWithoutStickyBit_Ignored()
    {
        var result = Select(new() { [Shared] = Dir(sticky: false, owner: 0), [SharedUser] = Dir() });

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: true)], result.Candidates);
        Assert.Contains(result.Rejections, r => r.Contains("sticky"));
    }

    [Fact]
    public void SharedTrashThatIsASymlink_Ignored()
    {
        var result = Select(new() { [Shared] = Symlink });

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: true)], result.Candidates);
        Assert.Contains(result.Rejections, r => r.Contains("symbolic link"));
    }

    [Fact]
    public void SharedTrashThatIsAFile_Ignored()
    {
        var result = Select(new() { [Shared] = RegularFile });

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: true)], result.Candidates);
    }

    [Fact]
    public void PerUserDirOwnedBySomeoneElse_Rejected()
    {
        var result = Select(new() { [Shared] = Dir(sticky: true, owner: 0), [SharedUser] = Dir(owner: 1001) });

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: true)], result.Candidates);
        Assert.Contains(result.Rejections, r => r.Contains("another user"));
    }

    [Fact]
    public void ExistingPrivateTrash_Used()
    {
        var result = Select(new() { [Private] = Dir() });

        Assert.Equal([new TrashDirectoryCandidate(Private, MustCreate: false)], result.Candidates);
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("file")]
    [InlineData("foreign")]
    public void UnsafePrivateTrash_NoCandidates(string kind)
    {
        FileSystemEntryStatus status = kind switch
        {
            "symlink" => Symlink,
            "file" => RegularFile,
            _ => Dir(owner: 0),
        };

        var result = Select(new() { [Private] = status });

        Assert.Empty(result.Candidates);
        Assert.NotEmpty(result.Rejections);
    }

    [Theory]
    [InlineData("/data", "/home/u", "/data/Trash")]
    [InlineData(null, "/home/u", "/home/u/.local/share/Trash")]
    [InlineData("", "/home/u", "/home/u/.local/share/Trash")]
    [InlineData("relative/data", "/home/u", "/home/u/.local/share/Trash")] // XDG: relative paths are invalid.
    [InlineData(null, null, null)]
    [InlineData(null, "relative", null)]
    public void HomeTrashDirectory(string? xdgDataHome, string? home, string? expected)
    {
        string? actual = TrashDirectorySelector.HomeTrashDirectory(xdgDataHome, home);

        // Path.Combine uses the host separator; normalise so the expectation reads the same on Windows.
        Assert.Equal(expected, actual?.Replace('\\', '/'));
    }
}
