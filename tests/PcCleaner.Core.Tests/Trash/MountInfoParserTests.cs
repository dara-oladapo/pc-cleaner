using PcCleaner.Core.Trash;

namespace PcCleaner.Core.Tests.Trash;

public class MountInfoParserTests
{
    // Shaped like a real Ubuntu table: optional fields of varying count, a bind mount, escaped names, a ro mount.
    private const string Sample =
        "22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw,errors=remount-ro\n"
        + "23 22 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw\n"
        + "40 22 8:17 / /media/user/My\\040Drive rw,nosuid,nodev,relatime shared:200 master:3 - ext4 /dev/sdb1 rw\n"
        + "41 22 8:33 / /mnt/back\\134slash\\011tab rw,relatime - vfat /dev/sdc1 rw,fmask=0022\n"
        + "42 22 8:49 / /mnt/cdrom ro,relatime shared:5 - iso9660 /dev/sr0 ro\n"
        + "43 22 8:2 /srv/data /mnt/bound rw,relatime shared:1 - ext4 /dev/sda2 rw\n"
        + "44 22 8:65 / /mnt/rofs rw,relatime - ext4 /dev/sde ro\n"
        + "45 22 8:81 / /mnt/a rw - ext4 /dev/sdf rw\n"
        + "46 22 8:97 / /mnt/ab rw - ext4 /dev/sdg rw\n"
        + "47 45 8:113 / /mnt/a rw - ext4 /dev/sdh rw\n"; // Stacked on top of 45.

    [Fact]
    public void Parse_ReadsAllFields()
    {
        var entries = MountInfoParser.Parse(Sample);

        Assert.Equal(10, entries.Count);
        var root = entries[0];
        Assert.Equal(22, root.MountId);
        Assert.Equal(1, root.ParentId);
        Assert.Equal(8u, root.DeviceMajor);
        Assert.Equal(2u, root.DeviceMinor);
        Assert.Equal("/", root.Root);
        Assert.Equal("/", root.MountPoint);
        Assert.Equal(["rw", "relatime"], root.MountOptions);
        Assert.Equal("ext4", root.FileSystemType);
        Assert.Equal("/dev/sda2", root.Source);
        Assert.Equal(["rw", "errors=remount-ro"], root.SuperOptions);
        Assert.False(root.IsReadOnly);
    }

    [Fact]
    public void Parse_UnescapesOctalSequences()
    {
        var entries = MountInfoParser.Parse(Sample);

        Assert.Equal("/media/user/My Drive", entries.Single(e => e.MountId == 40).MountPoint);
        Assert.Equal("/mnt/back\\slash\ttab", entries.Single(e => e.MountId == 41).MountPoint);
    }

    [Fact]
    public void Parse_HandlesMultipleAndZeroOptionalFields()
    {
        var entries = MountInfoParser.Parse(Sample);

        Assert.Equal("ext4", entries.Single(e => e.MountId == 40).FileSystemType); // shared:200 master:3
        Assert.Equal("vfat", entries.Single(e => e.MountId == 41).FileSystemType); // none
    }

    [Fact]
    public void Parse_BindMountKeepsRootWithinFileSystem()
    {
        Assert.Equal("/srv/data", MountInfoParser.Parse(Sample).Single(e => e.MountId == 43).Root);
    }

    [Fact]
    public void IsReadOnly_FromMountOrSuperblockOptions()
    {
        var entries = MountInfoParser.Parse(Sample);

        Assert.True(entries.Single(e => e.MountId == 42).IsReadOnly);  // ro mount
        Assert.True(entries.Single(e => e.MountId == 44).IsReadOnly);  // rw mount of a ro superblock
        Assert.False(entries.Single(e => e.MountId == 40).IsReadOnly);
    }

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("22 1 8:2 / / rw shared:1 ext4 /dev/sda2 rw")]          // No separator.
    [InlineData("x 1 8:2 / / rw - ext4 /dev/sda2 rw")]                  // Non-numeric id.
    [InlineData("22 1 82 / / rw - ext4 /dev/sda2 rw")]                  // Bad device.
    public void Parse_SkipsMalformedLines(string bad)
    {
        var entries = MountInfoParser.Parse(bad + "\n" + "22 1 8:2 / / rw - ext4 /dev/sda2 rw\n");
        Assert.Single(entries);
    }

    [Fact]
    public void Parse_ToleratesCrlfAndMissingTrailingNewline()
    {
        var entries = MountInfoParser.Parse("22 1 8:2 / / rw - ext4 /dev/sda2 rw\r\n23 22 8:3 / /home rw - ext4 /dev/sda3 rw");
        Assert.Equal(["/", "/home"], entries.Select(e => e.MountPoint));
        Assert.Equal(["rw"], entries[0].SuperOptions);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a\\040b", "a b")]
    [InlineData("\\134", "\\")]
    [InlineData("\\012", "\n")]
    [InlineData("\\303\\251", "é")]      // Octal-escaped UTF-8 bytes are recombined.
    [InlineData("café", "café")]    // Raw UTF-8 passes through.
    [InlineData("a\\9b", "a\\9b")]            // Not an octal escape: kept literally.
    [InlineData("trailing\\04", "trailing\\04")]
    public void Unescape(string field, string expected)
    {
        Assert.Equal(expected, MountInfoParser.Unescape(field));
    }

    [Theory]
    [InlineData("/home/user/file", 22)]
    [InlineData("/media/user/My Drive/movies/a.mkv", 40)]
    [InlineData("/media/user/My Drive", 40)]
    [InlineData("/media/user/My Drivex/a", 22)]   // Component-wise, not a string prefix.
    [InlineData("/mnt/ab/x", 46)]
    [InlineData("/mnt/a/x", 47)]                  // Stacked mount: the later one wins.
    public void FindContainingMount_LongestComponentPrefix(string path, int expectedMountId)
    {
        var entries = MountInfoParser.Parse(Sample);
        Assert.Equal(expectedMountId, MountInfoParser.FindContainingMount(entries, path)!.MountId);
    }

    [Fact]
    public void FindByMountId()
    {
        var entries = MountInfoParser.Parse(Sample);
        Assert.Equal("/mnt/cdrom", MountInfoParser.FindByMountId(entries, 42)!.MountPoint);
        Assert.Null(MountInfoParser.FindByMountId(entries, 999));
    }

    [Theory]
    [InlineData("/a/b", "/a", true)]
    [InlineData("/a", "/a", true)]
    [InlineData("/a", "/a/", true)]
    [InlineData("/ab", "/a", false)]
    [InlineData("/x", "/", true)]
    [InlineData("/a", "/a/b", false)]
    public void IsWithin(string path, string directory, bool expected)
    {
        Assert.Equal(expected, MountInfoParser.IsWithin(path, directory));
    }

    [Fact]
    public void Parse_RealMountInfo_OnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return; // /proc/self/mountinfo only exists on Linux.
        }

        var entries = MountInfoParser.Parse(File.ReadAllText("/proc/self/mountinfo"));

        Assert.NotEmpty(entries);
        Assert.Contains(entries, e => e.MountPoint == "/");
        Assert.All(entries, e => Assert.StartsWith("/", e.MountPoint));
        Assert.Equal("/", MountInfoParser.FindContainingMount(entries, "/")!.MountPoint);
    }
}
