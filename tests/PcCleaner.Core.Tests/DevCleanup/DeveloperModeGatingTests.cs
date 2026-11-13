using PcCleaner.Core.Abstractions;
using PcCleaner.Core.DevCleanup;
using PcCleaner.Core.Scanning;

namespace PcCleaner.Core.Tests.DevCleanup;

public class DeveloperModeGatingTests
{
    private static (DevCleanupService Service, FakeProcessRunner Runner) Create(bool developerMode)
    {
        var runner = new FakeProcessRunner(cmd => cmd switch
        {
            "docker info --format {{.ID}}" => FakeProcessRunner.Ok("id"),
            "docker system df --format {{json .}}" => FakeProcessRunner.Ok(string.Empty),
            "docker builder du" => FakeProcessRunner.Ok("Reclaimable:\t1GB"),
            _ => FakeProcessRunner.Fail("unexpected"),
        });
        IDevCleanupProvider[] providers =
        [
            new DockerCleanupProvider(runner, new RegistryWslDiskLocator(), checkWsl: false),
            new GitWorktreeProvider(runner, new DevCleanupOptions { Roots = [] }),
            new BuildArtifactProvider(runner, new JunkCleaner(), new DevCleanupOptions { Roots = [] }),
        ];
        var settings = new InMemoryAppSettings { DeveloperMode = developerMode };
        return (new DevCleanupService(providers, settings), runner);
    }

    [Fact]
    public async Task Off_MeasuresNothing_AndRunsNoCommand()
    {
        var (service, runner) = Create(developerMode: false);

        var reports = await service.MeasureAsync();

        Assert.Empty(reports);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Off_RefusesToClean_AndRunsNoCommand()
    {
        var (service, runner) = Create(developerMode: false);
        var item = new DevCleanupItem(DockerCleanupProvider.ProviderId, "Docker (host)", "Build cache", "r", 1, DevCleanupRisk.Low);

        DevCleanupResult result = await service.CleanAsync([item]);

        Assert.Empty(result.Succeeded);
        Assert.Contains("Developer mode is off", Assert.Single(result.Failed).Error);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task On_RunsProviders()
    {
        var (service, runner) = Create(developerMode: true);

        var reports = await service.MeasureAsync();

        Assert.Contains(reports, r => r.ProviderId == DockerCleanupProvider.ProviderId && r.Items.Count > 0);
        Assert.NotEmpty(runner.Calls);
    }

    [Fact]
    public async Task TurningOffAfterAScan_BlocksTheClean()
    {
        var runner = new FakeProcessRunner(_ => FakeProcessRunner.Ok());
        var settings = new InMemoryAppSettings { DeveloperMode = true };
        var service = new DevCleanupService([new DockerCleanupProvider(runner, new RegistryWslDiskLocator(), checkWsl: false)], settings);
        var item = new DevCleanupItem(DockerCleanupProvider.ProviderId, "Docker (host)", "Build cache", "r", 1, DevCleanupRisk.Low);

        settings.DeveloperMode = false;
        await service.CleanAsync([item]);

        Assert.Empty(runner.Calls);
    }

    [Theory]
    [InlineData(false, false, true, false, true)]
    [InlineData(false, false, false, true, true)]
    [InlineData(false, false, false, false, false)]
    [InlineData(false, true, true, true, false)]
    [InlineData(true, false, true, true, false)]
    public void Suggestion_ShownOnce_OnlyWhenOffAndToolsInstalled(bool on, bool shown, bool git, bool docker, bool expected)
    {
        var settings = new InMemoryAppSettings { DeveloperMode = on, DeveloperModeSuggestionShown = shown };

        bool suggest = DeveloperModeAdvisor.ShouldSuggest(settings, name => name switch
        {
            "git" => git,
            "docker" => docker,
            _ => false,
        });

        Assert.Equal(expected, suggest);
        Assert.Equal(on, settings.DeveloperMode);
    }

    [Fact]
    public void DeveloperMode_DefaultsToOff()
    {
        Assert.False(new InMemoryAppSettings().DeveloperMode);
    }
}
