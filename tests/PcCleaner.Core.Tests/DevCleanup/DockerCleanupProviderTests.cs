using PcCleaner.Core.DevCleanup;

namespace PcCleaner.Core.Tests.DevCleanup;

/// <summary>Every Docker and WSL command here is answered by a fake. Nothing reaches a real engine.</summary>
public class DockerCleanupProviderTests
{
    private const string HostDf =
        """
        {"Active":"23","Reclaimable":"13.5GB (47%)","Size":"28.43GB","TotalCount":"97","Type":"Images"}
        {"Active":"6","Reclaimable":"1.922GB (99%)","Size":"1.922GB","TotalCount":"40","Type":"Containers"}
        {"Active":"16","Reclaimable":"3.503GB (55%)","Size":"6.361GB","TotalCount":"55","Type":"Local Volumes"}
        {"Active":"14","Reclaimable":"20.32GB","Size":"50.03GB","TotalCount":"729","Type":"Build Cache"}
        """;

    private const string WslList =
        """
          NAME              STATE           VERSION
        * Ubuntu            Running         2
          Old               Stopped         2
          docker-desktop    Running         2
        """;

    private const string Corrupt = "Error response from daemon: failed to retrieve container list: rw layer snapshot not found for container c2f3";

    private sealed class FakeDisks(params VirtualDiskInfo[] disks) : IWslDiskLocator
    {
        public IReadOnlyList<VirtualDiskInfo> FindDisks() => disks;
    }

    private static FakeProcessRunner HostOnly() => new(cmd => cmd switch
    {
        "docker info --format {{.ID}}" => FakeProcessRunner.Ok("host-id\n"),
        "docker system df --format {{json .}}" => FakeProcessRunner.Ok(HostDf),
        _ => null,
    });

    [Fact]
    public async Task Host_OffersFourItems_WithDefaultsAndRisks()
    {
        var provider = new DockerCleanupProvider(HostOnly(), new FakeDisks(), checkWsl: false);

        DevCleanupReport report = await provider.MeasureAsync();

        Assert.Empty(report.Errors);
        var byTitle = report.Items.ToDictionary(i => i.Title);
        DevCleanupItem build = byTitle["Build cache unused for 7+ days"];
        DevCleanupItem images = byTitle["Unused images older than 7 days"];
        DevCleanupItem containers = byTitle["Stopped containers"];
        DevCleanupItem volumes = byTitle["Unused anonymous volumes"];

        Assert.Equal(20_320_000_000, build.SizeBytes);
        Assert.True(build.SelectedByDefault);
        Assert.True(containers.SelectedByDefault);
        Assert.Contains("34 stopped", containers.Reason);
        Assert.False(images.SelectedByDefault);
        Assert.False(volumes.SelectedByDefault);
        Assert.Equal(DevCleanupRisk.High, volumes.Risk);
        Assert.NotNull(volumes.Warning);
        Assert.All(report.Items, i => Assert.Equal("Docker (host)", i.Group));
    }

    [Fact]
    public async Task WslEngine_PartlyCorrupt_ReportsStderrVerbatim_AndFallsBackForBuildCache()
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "docker info --format {{.ID}}" => ProcessResult.NotStarted("docker not installed"),
            "wsl -l -v" => FakeProcessRunner.Ok(WslList),
            "wsl -d Ubuntu --exec docker info --format {{.ID}}" => FakeProcessRunner.Ok("ubuntu-id"),
            "wsl -d Ubuntu --exec docker system df --format {{json .}}" => FakeProcessRunner.Fail(Corrupt),
            "wsl -d Ubuntu --exec docker builder du" => FakeProcessRunner.Ok("ID  RECLAIMABLE\nShared:\t\t479.2GB\nPrivate:\t172.9GB\nReclaimable:\t647.3GB\nTotal:\t\t652.2GB\n"),
            _ => null,
        });
        var disk = new VirtualDiskInfo("Ubuntu", @"C:\wsl\ext4.vhdx", 935_000_000_000);
        var provider = new DockerCleanupProvider(runner, new FakeDisks(disk), checkWsl: true);

        DevCleanupReport report = await provider.MeasureAsync();

        Assert.Contains(report.Errors, e => e.Contains(Corrupt, StringComparison.Ordinal));
        DevCleanupItem build = report.Items.Single(i => i.Title.StartsWith("Build cache", StringComparison.Ordinal));
        Assert.Equal(647_300_000_000, build.SizeBytes);
        Assert.Equal("Docker (WSL: Ubuntu)", build.Group);
        Assert.Contains(report.Notes, n => n.Contains("does not shrink", StringComparison.Ordinal));
        Assert.Contains(report.Notes, n => n.Contains("'Old' is not running", StringComparison.Ordinal));
        Assert.Equal(disk, Assert.Single(report.VirtualDisks));

        // A stopped distro is not started, and Docker Desktop's own distros are not probed.
        Assert.DoesNotContain(runner.Calls, c => c.StartsWith("wsl -d Old", StringComparison.Ordinal));
        Assert.DoesNotContain(runner.Calls, c => c.StartsWith("wsl -d docker-desktop", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SameEngineSeenFromHostAndWsl_IsCountedOnce()
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "docker info --format {{.ID}}" => FakeProcessRunner.Ok("shared-id"),
            "docker system df --format {{json .}}" => FakeProcessRunner.Ok(HostDf),
            "wsl -l -v" => FakeProcessRunner.Ok(WslList),
            "wsl -d Ubuntu --exec docker info --format {{.ID}}" => FakeProcessRunner.Ok("shared-id"),
            _ => null,
        });

        DevCleanupReport report = await new DockerCleanupProvider(runner, new FakeDisks(), checkWsl: true).MeasureAsync();

        Assert.All(report.Items, i => Assert.Equal("Docker (host)", i.Group));
        Assert.DoesNotContain(runner.Calls, c => c.Contains("--exec docker system df", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DistroWithoutDocker_IsSkippedQuietly()
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "wsl -l -v" => FakeProcessRunner.Ok(WslList),
            "wsl -d Ubuntu --exec docker info --format {{.ID}}" =>
                FakeProcessRunner.Fail("<3>WSL (1 - Relay) ERROR: CreateProcessCommon:818: execvpe(docker) failed: No such file or directory"),
            _ => null,
        });

        DevCleanupReport report = await new DockerCleanupProvider(runner, new FakeDisks(), checkWsl: true).MeasureAsync();

        Assert.Empty(report.Items);
        Assert.Empty(report.Errors);
    }

    [Fact]
    public async Task Clean_RunsTheExactPruneCommands_AndSurfacesFailures()
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "docker info --format {{.ID}}" => FakeProcessRunner.Ok("host-id"),
            "docker system df --format {{json .}}" => FakeProcessRunner.Ok(HostDf),
            "docker builder prune -f --filter until=168h" => FakeProcessRunner.Ok("ID\tRECLAIMABLE\nTotal:\t1.5GB\n"),
            "docker container prune -f" => FakeProcessRunner.Fail(Corrupt),
            _ => null,
        });
        var provider = new DockerCleanupProvider(runner, new FakeDisks(), checkWsl: false);
        var defaults = (await provider.MeasureAsync()).Items.Where(i => i.SelectedByDefault).ToList();

        DevCleanupResult result = await provider.CleanAsync(defaults);

        Assert.Equal(1_500_000_000, result.BytesFreed);
        Assert.Equal("Build cache unused for 7+ days", Assert.Single(result.Succeeded).Title);
        Assert.Contains(Corrupt, Assert.Single(result.Failed).Error);
        Assert.DoesNotContain(runner.Calls, c => c.Contains("image prune", StringComparison.Ordinal) || c.Contains("volume prune", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Clean_InWsl_UsesExecSoTheDistroShellIsBypassed()
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "wsl -l -v" => FakeProcessRunner.Ok(WslList),
            "wsl -d Ubuntu --exec docker info --format {{.ID}}" => FakeProcessRunner.Ok("u"),
            "wsl -d Ubuntu --exec docker system df --format {{json .}}" => FakeProcessRunner.Ok(HostDf),
            "wsl -d Ubuntu --exec docker image prune -a -f --filter until=168h" => FakeProcessRunner.Ok("Total reclaimed space: 2GB"),
            _ => null,
        });
        var provider = new DockerCleanupProvider(runner, new FakeDisks(), checkWsl: true);
        DevCleanupItem images = (await provider.MeasureAsync()).Items.Single(i => i.Title.StartsWith("Unused images", StringComparison.Ordinal));

        DevCleanupResult result = await provider.CleanAsync([images]);

        Assert.Equal(2_000_000_000, result.BytesFreed);
    }

    [Theory]
    [InlineData("13.5GB (47%)", 13_500_000_000L)]
    [InlineData("1.922GB", 1_922_000_000L)]
    [InlineData("512kB", 512_000L)]
    [InlineData("0B", 0L)]
    [InlineData("2GiB", 2_147_483_648L)]
    [InlineData("N/A", null)]
    public void ParseSize_HandlesDockerUnits(string text, long? expected) =>
        Assert.Equal(expected, DockerOutputParser.ParseSize(text));

    [Fact]
    public void ParseWslList_ReadsNamesStatesAndDefault()
    {
        var distros = DockerOutputParser.ParseWslList(WslList);

        Assert.Equal(["Ubuntu", "Old", "docker-desktop"], distros.Select(d => d.Name).ToArray());
        Assert.True(distros[0].IsDefault);
        Assert.True(distros[0].IsRunning);
        Assert.False(distros[1].IsRunning);
    }
}
