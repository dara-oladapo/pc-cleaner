using PcCleaner.Core.Models;
using PcCleaner.Core.Scanning;
using static PcCleaner.Core.Tests.CloudTestData;

namespace PcCleaner.Core.Tests;

public class CloudSpaceServiceTests
{
    private static readonly TimeSpan MinAge = CloudEligibilityRules.DefaultMinimumAge;
    private static readonly CloudSpaceOptions FastOptions = new(3, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(1));

    private static (CloudSpaceService Service, FakeCloudDriveScanner Scanner, FakeCloudFileReleaser Releaser) Create(
        IReadOnlyList<CloudFileState> files,
        CloudSyncRoot? root = null,
        CloudSpaceOptions? options = null)
    {
        var scanner = new FakeCloudDriveScanner([root ?? Root()], files);
        var releaser = new FakeCloudFileReleaser(files);
        var service = new CloudSpaceService(scanner, releaser, options ?? FastOptions, new FixedTimeProvider(Now));
        return (service, scanner, releaser);
    }

    private static List<CloudReleaseRequest> Requests(CloudRootScan scan) =>
        scan.Eligible.Select(f => new CloudReleaseRequest(scan.Root, f.Path)).ToList();

    [Fact]
    public async Task Scan_AddsUpHydratedAndEligibleSeparately()
    {
        CloudFileState[] files =
        [
            Eligible("a.mp4", 1000),
            Eligible("b.zip", 500),
            Eligible("pinned.iso", 4000) with { PinState = CloudPinState.Pinned },
            Eligible("dirty.docx", 300) with { SyncState = CloudSyncState.NotInSync },
            Eligible("online.pdf", 0) with { IsHydrated = false, AllocatedBytes = 0 },
        ];
        var (service, _, _) = Create(files);

        CloudRootScan scan = await service.ScanAsync(Root(), MinAge);

        Assert.Equal(5800, scan.HydratedBytes);
        Assert.Equal(4, scan.HydratedCount);
        Assert.Equal(1500, scan.EligibleBytes);
        Assert.Equal(2, scan.EligibleCount);
        Assert.Equal(4000, scan.IneligibleBytesByReason[CloudIneligibility.Pinned]);
        Assert.Equal(300, scan.IneligibleBytesByReason[CloudIneligibility.NotInSync]);
        Assert.DoesNotContain(CloudIneligibility.NotHydrated, scan.IneligibleBytesByReason.Keys);
    }

    [Fact]
    public async Task Scan_ReturnsTheLargestFilesBiggestFirst_IncludingIneligibleOnes()
    {
        CloudFileState[] files =
        [
            Eligible("1", 10),
            Eligible("2", 50),
            Eligible("3", 30) with { PinState = CloudPinState.Pinned },
            Eligible("4", 40),
            Eligible("5", 20),
        ];
        var (service, _, _) = Create(files);

        CloudRootScan scan = await service.ScanAsync(Root(), MinAge);

        Assert.Equal([50L, 40L, 30L], scan.LargestFiles.Select(v => v.File.AllocatedBytes));
        Assert.False(scan.LargestFiles[2].IsEligible);
        Assert.Equal(CloudIneligibility.Pinned, scan.LargestFiles[2].Reason);
    }

    [Fact]
    public async Task Scan_ProbesOnlyFilesThatPassEveryOtherRule()
    {
        CloudFileState[] files =
        [
            Eligible("candidate.bin"),
            Eligible("recent.bin") with { LastAccessUtc = Now.AddDays(-1) },
            Eligible("pinned.bin") with { PinState = CloudPinState.Pinned },
        ];
        var (service, scanner, _) = Create(files);

        await service.ScanAsync(Root(), MinAge);

        Assert.Equal([files[0].Path], scanner.Probed);
    }

    [Fact]
    public async Task Scan_FileOpenElsewhere_IsNotEligible()
    {
        CloudFileState file = Eligible("open.xlsx", 700);
        var (service, scanner, _) = Create([file]);
        scanner.OpenFiles.Add(file.Path);

        CloudRootScan scan = await service.ScanAsync(Root(), MinAge);

        Assert.Equal(0, scan.EligibleBytes);
        Assert.Equal(700, scan.IneligibleBytesByReason[CloudIneligibility.InUse]);
    }

    [Fact]
    public async Task Release_ReportsMeasuredBytesReclaimed()
    {
        CloudFileState[] files = [Eligible("a", 1000), Eligible("b", 3000)];
        var (service, _, releaser) = Create(files);
        CloudRootScan scan = await service.ScanAsync(Root(), MinAge);

        CloudReleaseResult result = await service.ReleaseAsync(Requests(scan), MinAge);

        Assert.Equal(4000, result.BytesReclaimed);
        Assert.Equal(0, result.BytesPending);
        Assert.Equal(2, result.ReleasedPaths.Count);
        Assert.Equal(2, releaser.Requested.Count);
    }

    [Fact]
    public async Task Release_WaitsForASlowProvider()
    {
        CloudFileState[] files = [Eligible("a", 1000)];
        var (service, _, releaser) = Create(files);
        releaser.DehydrateAfterMeasurements = 3;

        CloudReleaseResult result = await service.ReleaseAsync([new CloudReleaseRequest(Root(), files[0].Path)], MinAge);

        Assert.Equal(1000, result.BytesReclaimed);
        Assert.Empty(result.PendingPaths);
    }

    [Fact]
    public async Task Release_ProviderThatHasNotReleasedYet_IsReportedPendingNotFreed()
    {
        CloudFileState[] files = [Eligible("a", 1000)];
        var options = FastOptions with { SettleTimeout = TimeSpan.Zero };
        var (service, _, releaser) = Create(files, options: options);
        releaser.DehydrateAfterMeasurements = null;

        CloudReleaseResult result = await service.ReleaseAsync([new CloudReleaseRequest(Root(), files[0].Path)], MinAge);

        Assert.Equal(0, result.BytesReclaimed);
        Assert.Equal(1000, result.BytesPending);
        Assert.Equal([files[0].Path], result.PendingPaths);
    }

    [Fact]
    public async Task Release_RechecksEveryRule_AndSkipsFilesThatChangedSinceTheScan()
    {
        CloudFileState[] files = [Eligible("pinned-later"), Eligible("edited-later"), Eligible("opened-later"), Eligible("still-ok")];
        var (service, _, releaser) = Create(files);
        CloudRootScan scan = await service.ScanAsync(Root(), MinAge);
        Assert.Equal(4, scan.EligibleCount);

        releaser.Change(files[0].Path, f => f with { PinState = CloudPinState.Pinned });
        releaser.Change(files[1].Path, f => f with { SyncState = CloudSyncState.NotInSync, LastWriteUtc = Now });
        releaser.Change(files[2].Path, f => f with { IsInUse = true });

        CloudReleaseResult result = await service.ReleaseAsync(Requests(scan), MinAge);

        Assert.Equal([files[3].Path], releaser.Requested);
        Assert.Equal(
            [CloudIneligibility.Pinned, CloudIneligibility.NotInSync, CloudIneligibility.InUse],
            result.Skipped.Select(s => s.SkipReason));
    }

    [Fact]
    public async Task Release_InReadOnlyRoot_RequestsNothing()
    {
        CloudFileState[] files = [Eligible("a")];
        CloudSyncRoot readOnly = Root(canFreeUp: false);
        var (service, _, releaser) = Create(files, readOnly);

        CloudReleaseResult result = await service.ReleaseAsync([new CloudReleaseRequest(readOnly, files[0].Path)], MinAge);

        Assert.Empty(releaser.Requested);
        Assert.Equal(CloudIneligibility.OutsideReleasableRoot, Assert.Single(result.Skipped).SkipReason);
    }

    [Theory]
    [InlineData("..", "escape.txt")]
    [InlineData("", "")]
    public async Task Release_PathOutsideTheRoot_IsRefused(string up, string name)
    {
        string path = up.Length == 0
            ? RootPath
            : RootPath + Path.DirectorySeparatorChar + up + Path.DirectorySeparatorChar + name;
        var (service, _, releaser) = Create([Eligible() with { Path = path }]);

        CloudReleaseResult result = await service.ReleaseAsync([new CloudReleaseRequest(Root(), path)], MinAge);

        Assert.Empty(releaser.Requested);
        Assert.Single(result.Skipped);
    }

    [Fact]
    public void IsReleasable_RejectsSiblingFolderWithSharedPrefix()
    {
        // "OneDrive - Work" starts with "OneDrive" but is not inside it.
        string sibling = RootPath + " - Work" + Path.DirectorySeparatorChar + "a.txt";
        Assert.False(CloudSpaceService.IsReleasable(new CloudReleaseRequest(Root(), sibling)));
        Assert.True(CloudSpaceService.IsReleasable(new CloudReleaseRequest(Root(), Path.Combine(RootPath, "sub", "a.txt"))));
    }

    [Fact]
    public async Task Release_PlatformFailure_IsReportedAndOthersContinue()
    {
        CloudFileState[] files = [Eligible("bad", 10), Eligible("good", 20)];
        var (service, _, releaser) = Create(files);
        releaser.FailingPaths.Add(files[0].Path);

        CloudReleaseResult result = await service.ReleaseAsync(
            files.Select(f => new CloudReleaseRequest(Root(), f.Path)).ToList(), MinAge);

        Assert.Equal(files[0].Path, Assert.Single(result.Failed).Path);
        Assert.Equal(20, result.BytesReclaimed);
    }

    [Fact]
    public async Task Release_DuplicateRequests_AreRequestedOnce()
    {
        CloudFileState file = Eligible("a", 100);
        var (service, _, releaser) = Create([file]);

        CloudReleaseResult result = await service.ReleaseAsync(
            [new CloudReleaseRequest(Root(), file.Path), new CloudReleaseRequest(Root(), file.Path)], MinAge);

        Assert.Single(releaser.Requested);
        Assert.Equal(100, result.BytesReclaimed);
    }

    [Fact]
    public async Task Release_Cancelled_StopsBeforeTheNextFile()
    {
        CloudFileState[] files = [Eligible("a"), Eligible("b")];
        var (service, _, releaser) = Create(files);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        CloudReleaseResult result = await service.ReleaseAsync(
            files.Select(f => new CloudReleaseRequest(Root(), f.Path)).ToList(), MinAge, cts.Token);

        Assert.Empty(releaser.Requested);
        Assert.Equal(0, result.BytesReclaimed);
    }
}
