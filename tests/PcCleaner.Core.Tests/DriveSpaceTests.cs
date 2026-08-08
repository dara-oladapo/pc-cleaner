using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests;

public class DriveSpaceTests
{
    private static DriveSpace Drive(long total, long free) => new("C:\\", total, free);

    [Fact]
    public void UsedBytes_IsTotalMinusFree()
    {
        Assert.Equal(300, Drive(total: 1000, free: 700).UsedBytes);
    }

    [Fact]
    public void Shares_SumToOne_SoTheCapacityBarFillsExactly()
    {
        var drive = Drive(total: 1000, free: 400);

        double sum = drive.OccupiedShare(200) + drive.ReclaimableShare(200) + drive.FreeShare;

        Assert.Equal(1.0, sum, precision: 10);
    }

    [Fact]
    public void ReclaimableShare_IsCarvedOutOfTheOccupiedSegment()
    {
        var drive = Drive(total: 1000, free: 400);

        // 600 used; reclaiming 150 of it leaves 450 still occupied.
        Assert.Equal(0.45, drive.OccupiedShare(150), precision: 10);
        Assert.Equal(0.15, drive.ReclaimableShare(150), precision: 10);
    }

    [Fact]
    public void ReclaimableLargerThanUsedSpace_IsClampedRatherThanOverflowingTheBar()
    {
        var drive = Drive(total: 1000, free: 900);

        // Only 100 bytes are actually in use, so a stale 5000-byte scan total can't draw past the drive.
        Assert.Equal(0, drive.OccupiedShare(5000), precision: 10);
        Assert.Equal(0.1, drive.ReclaimableShare(5000), precision: 10);
    }

    [Fact]
    public void NegativeReclaimable_IsTreatedAsZero()
    {
        var drive = Drive(total: 1000, free: 400);

        Assert.Equal(0, drive.ReclaimableShare(-50), precision: 10);
        Assert.Equal(0.6, drive.OccupiedShare(-50), precision: 10);
    }

    [Fact]
    public void ZeroCapacityDrive_ReportsZeroSharesInsteadOfDividingByZero()
    {
        var drive = Drive(total: 0, free: 0);

        Assert.Equal(0, drive.OccupiedShare(10));
        Assert.Equal(0, drive.ReclaimableShare(10));
        Assert.Equal(0, drive.FreeShare);
    }

    [Fact]
    public void Reader_ReturnsNull_WhenTheProfilePathIsUnusable()
    {
        var reader = new DriveSpaceReader(() => string.Empty);

        Assert.Null(reader.Read());
    }

    [Fact]
    public void Reader_ReportsPositiveCapacity_ForTheDriveThisTestRunsOn()
    {
        var reader = new DriveSpaceReader();

        var space = reader.Read();

        // A CI agent always has a readable profile drive; if this ever returns null the dashboard silently
        // loses its capacity bar, which is worth failing a build over.
        Assert.NotNull(space);
        Assert.True(space!.TotalBytes > 0);
        Assert.True(space.FreeBytes >= 0);
        Assert.True(space.FreeBytes <= space.TotalBytes);
    }
}
