using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

/// <summary>Records every invocation and answers from a script. Never starts a process.</summary>
internal sealed class FakeSystemctl : ISystemctlRunner
{
    private readonly Func<IReadOnlyList<string>, SystemctlResult> _respond;

    public FakeSystemctl(Func<IReadOnlyList<string>, SystemctlResult> respond) => _respond = respond;

    public List<IReadOnlyList<string>> Calls { get; } = [];

    public Task<SystemctlResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        Calls.Add(arguments.ToList());
        return Task.FromResult(_respond(arguments));
    }

    public static SystemctlResult Ok(string stdout = "") => new(true, 0, stdout, string.Empty);

    public static SystemctlResult Fail(string stderr, int exitCode = 1) => new(true, exitCode, string.Empty, stderr);
}

public class SystemdUserUnitsTests
{
    private const string ListJson = """
        [{"unit_file":"syncthing.service","state":"enabled","preset":"enabled"},
         {"unit_file":"backup.service","state":"disabled","preset":"enabled"},
         {"unit_file":"dbus.service","state":"static","preset":null},
         {"unit_file":"app-foo@autostart.service","state":"generated","preset":null},
         {"unit_file":"getty@.service","state":"disabled","preset":null},
         {"unit_file":"alias.service","state":"alias","preset":null},
         {"unit_file":"gnome-session.target","state":"static","preset":null}]
        """;

    private const string Show =
        "Id=syncthing.service\nDescription=Syncthing - Open Source Continuous File Synchronization\n" +
        "ExecStart={ path=/usr/bin/syncthing ; argv[]=/usr/bin/syncthing serve --no-browser ; ignore_errors=no }\n" +
        "FragmentPath=/usr/lib/systemd/user/syncthing.service\n\n" +
        "Id=backup.service\nDescription=\nExecStart=\nFragmentPath=/home/u/.config/systemd/user/backup.service\n";

    private static SystemctlResult Respond(IReadOnlyList<string> args) => args[1] switch
    {
        "list-unit-files" => FakeSystemctl.Ok(ListJson),
        "show" => FakeSystemctl.Ok(Show),
        _ => FakeSystemctl.Fail("unexpected"),
    };

    [Fact]
    public async Task List_KeepsOnlyToggleableServices_WithDetails()
    {
        var fake = new FakeSystemctl(Respond);

        var units = await new SystemdUserUnits(fake).ListAsync();

        Assert.Equal(["syncthing.service", "backup.service"], units.Select(u => u.Unit));

        var syncthing = units[0];
        Assert.Equal("Syncthing - Open Source Continuous File Synchronization", syncthing.Description);
        Assert.Equal("/usr/bin/syncthing serve --no-browser", syncthing.Command);
        Assert.Equal("/usr/bin/syncthing", syncthing.Program);
        Assert.True(syncthing.IsEnabled);

        var backup = units[1];
        Assert.Equal("backup", backup.Description);
        Assert.Equal("/home/u/.config/systemd/user/backup.service", backup.Command);
        Assert.False(backup.IsEnabled);
    }

    [Fact]
    public async Task List_PassesArgumentsAsAList_WithUserScope()
    {
        var fake = new FakeSystemctl(Respond);

        await new SystemdUserUnits(fake).ListAsync();

        Assert.Equal(
            ["--user", "list-unit-files", "--type=service", "--no-legend", "--no-pager", "--output=json"],
            fake.Calls[0]);
        Assert.Equal(
            ["--user", "show", "--no-pager", "-p", "Id,Description,ExecStart,FragmentPath", "--", "syncthing.service", "backup.service"],
            fake.Calls[1]);
    }

    [Fact]
    public async Task List_FallsBackToTextWhenJsonIsUnsupported()
    {
        var fake = new FakeSystemctl(args => args[1] switch
        {
            "list-unit-files" when args.Contains("--output=json") => FakeSystemctl.Fail("Unknown output 'json'."),
            "list-unit-files" => FakeSystemctl.Ok("syncthing.service enabled enabled\ndbus.service static -\n"),
            _ => FakeSystemctl.Fail("show unsupported"),
        });

        var units = await new SystemdUserUnits(fake).ListAsync();

        var unit = Assert.Single(units);
        Assert.Equal("syncthing.service", unit.Unit);
        Assert.Equal("syncthing", unit.Description); // show failed: names alone still make a list
        Assert.True(unit.IsEnabled);
    }

    [Fact]
    public async Task List_NoUserManager_IsEmptyWithError()
    {
        var fake = new FakeSystemctl(_ => FakeSystemctl.Fail("Failed to connect to bus: No medium found"));
        var systemd = new SystemdUserUnits(fake);

        Assert.Empty(await systemd.ListAsync());
        Assert.Equal("Failed to connect to bus: No medium found", systemd.LastListError);
    }

    [Fact]
    public async Task List_SystemctlMissing_IsEmpty()
    {
        var fake = new FakeSystemctl(_ => SystemctlResult.NotStarted("No such file or directory"));
        Assert.Empty(await new SystemdUserUnits(fake).ListAsync());
    }

    [Theory]
    [InlineData(true, "enable")]
    [InlineData(false, "disable")]
    public async Task SetEnabled_RunsEnableOrDisable_ThenVerifies(bool enabled, string verb)
    {
        var fake = new FakeSystemctl(args => args.Contains("is-enabled")
            ? FakeSystemctl.Ok(enabled ? "enabled\n" : "disabled\n")
            : FakeSystemctl.Ok());

        await new SystemdUserUnits(fake).SetEnabledAsync("syncthing.service", enabled);

        Assert.Equal(["--user", "--no-pager", verb, "--", "syncthing.service"], fake.Calls[0]);
        Assert.Equal(["--user", "is-enabled", "--", "syncthing.service"], fake.Calls[1]);
    }

    [Fact]
    public async Task Disable_GloballyEnabledUnit_ThrowsWithHint()
    {
        var fake = new FakeSystemctl(args => args.Contains("is-enabled") ? FakeSystemctl.Ok("enabled\n") : FakeSystemctl.Ok());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SystemdUserUnits(fake).SetEnabledAsync("pipewire.service", false));
        Assert.Contains("--global", ex.Message);
    }

    [Fact]
    public async Task SetEnabled_Failure_SurfacesStderr()
    {
        var fake = new FakeSystemctl(_ => FakeSystemctl.Fail("Failed to enable unit: Unit file x.service does not exist.\n"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SystemdUserUnits(fake).SetEnabledAsync("x.service", true));
        Assert.Equal("Failed to enable unit: Unit file x.service does not exist.", ex.Message);
    }

    [Theory]
    [InlineData("x.service; rm -rf ~")]
    [InlineData("$(evil).service")]
    [InlineData("a b.service")]
    [InlineData("x.target")]
    [InlineData("")]
    public async Task SetEnabled_RejectsInvalidNames_WithoutRunningAnything(string unit)
    {
        var fake = new FakeSystemctl(_ => FakeSystemctl.Ok());

        await Assert.ThrowsAsync<ArgumentException>(() => new SystemdUserUnits(fake).SetEnabledAsync(unit, false));
        Assert.Empty(fake.Calls);
    }
}
