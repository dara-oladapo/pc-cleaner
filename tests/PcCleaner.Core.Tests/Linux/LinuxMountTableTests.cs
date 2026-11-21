using PcCleaner.Core.Platform.Linux;

namespace PcCleaner.Core.Tests.Linux;

public class LinuxMountTableTests
{
    // Trimmed from a real Ubuntu /proc/self/mountinfo, plus a mount point with a space in it.
    private const string MountInfo =
        "22 1 8:2 / / rw,relatime shared:1 - ext4 /dev/sda2 rw\n" +
        "23 22 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw\n" +
        "24 22 0:22 / /sys rw,nosuid,nodev,noexec,relatime shared:2 - sysfs sysfs rw\n" +
        "25 22 0:5 / /dev rw,nosuid,relatime shared:3 - devtmpfs udev rw,size=8000000k\n" +
        "40 22 0:35 / /home/ada/.cache rw,relatime shared:20 - tmpfs tmpfs rw\n" +
        "41 40 8:17 / /home/ada/.cache/big\\040disk rw,relatime shared:21 - ext4 /dev/sdb1 rw\n" +
        "42 22 0:50 / /home/ada/odd rw,relatime shared:30 master:4 - proc proc rw\n" +
        "garbage line\n" +
        "\n";

    [Fact]
    public void ParseMountInfo_ReadsMountPointsAndTypes_AndSkipsGarbage()
    {
        var table = LinuxMountTable.ParseMountInfo(MountInfo);

        Assert.Equal(7, table.Mounts.Count);
        Assert.Contains(new LinuxMount("/home/ada/.cache/big disk", "ext4"), table.Mounts);
        Assert.Contains(new LinuxMount("/proc", "proc"), table.Mounts);
        Assert.Contains(new LinuxMount("/home/ada/odd", "proc"), table.Mounts); // optional fields before "-"
    }

    [Fact]
    public void FindContaining_PicksLongestMountPoint()
    {
        var table = LinuxMountTable.ParseMountInfo(MountInfo);

        Assert.Equal("tmpfs", table.FindContaining("/home/ada/.cache/pip")!.FileSystemType);
        Assert.Equal("/", table.FindContaining("/home/ada/.cachet")!.MountPoint);
        Assert.Equal("/home/ada/.cache/big disk", table.FindContaining("/home/ada/.cache/big disk/x")!.MountPoint);
    }

    [Fact]
    public void MountPointsBeneath_IsStrict()
    {
        var table = LinuxMountTable.ParseMountInfo(MountInfo);

        Assert.Equal(new[] { "/home/ada/.cache/big disk" }, table.MountPointsBeneath("/home/ada/.cache"));
        Assert.Empty(table.MountPointsBeneath("/home/ada/.cache/big disk"));
        Assert.Contains("/home/ada/.cache", table.MountPointsBeneath("/home/ada"));
    }

    [Theory]
    [InlineData("/proc/self", true)]
    [InlineData("/sys/kernel", true)]
    [InlineData("/dev/shm", true)]
    [InlineData("/home/ada/odd/1", true)]   // a proc mount somewhere unusual is still proc
    [InlineData("/home/ada/.cache", false)] // tmpfs is a real file system for this purpose
    [InlineData("/devices", false)]         // prefix, not path, match
    public void IsPseudoFileSystem(string path, bool expected)
    {
        var table = LinuxMountTable.ParseMountInfo(MountInfo);

        Assert.Equal(expected, table.IsPseudoFileSystem(path));
    }

    [Fact]
    public void IsPseudoFileSystem_RefusesProcSysDevByPath_EvenWithAnEmptyTable()
    {
        var table = new LinuxMountTable([]);

        Assert.True(table.IsPseudoFileSystem("/proc"));
        Assert.False(table.IsPseudoFileSystem("/home/ada"));
    }
}
