using PcCleaner.Core.Models;
using PcCleaner.Core.Startup.Linux;

namespace PcCleaner.Core.Tests.Linux;

public sealed class LinuxStartupItemManagerTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    private static XdgAutostartEntry Entry(string id, string exec, bool enabled = true, StartupItemScope scope = StartupItemScope.CurrentUser) =>
        new(id, "/x/" + id, scope, "Name of " + id, exec, DesktopEntryRules.GetProgram(exec),
            enabled ? AutostartInactiveReasons.None : AutostartInactiveReasons.Hidden);

    private static SystemdUserUnit Unit(string unit, string command, bool enabled = true) =>
        new(unit, "Desc of " + unit, command, DesktopEntryRules.GetProgram(command), enabled);

    [Fact]
    public void Merge_DistinctItems_AreAllListed()
    {
        var items = LinuxStartupItemManager.Merge(
            [Entry("tray.desktop", "/usr/bin/tray")],
            [Unit("syncthing.service", "/usr/bin/syncthing serve")]);

        Assert.Equal(2, items.Count);
        var unit = items.Single(i => i.Source == StartupItemSource.LinuxSystemdUser);
        Assert.Equal("systemd-user:syncthing.service", unit.Id);
        Assert.Equal("Desc of syncthing.service", unit.Name);
        Assert.Equal(StartupItemScope.CurrentUser, unit.Scope);

        var xdg = items.Single(i => i.Source == StartupItemSource.LinuxXdgAutostart);
        Assert.Equal("xdg:tray.desktop", xdg.Id);
        Assert.Equal("/usr/bin/tray", xdg.CommandOrPath);
    }

    [Theory]
    [InlineData("syncthing.desktop", "/usr/bin/something-else")] // same name
    [InlineData("syncthing-start.desktop", "/usr/bin/syncthing -no-browser")] // same program
    [InlineData("st.desktop", "syncthing")] // program named like the unit
    public void Merge_SameProgramInBothMechanisms_IsOneItem(string desktopId, string exec)
    {
        var items = LinuxStartupItemManager.Merge(
            [Entry(desktopId, exec, enabled: false)],
            [Unit("syncthing.service", "/usr/bin/syncthing serve", enabled: true)]);

        var item = Assert.Single(items);
        Assert.Equal($"systemd-user:syncthing.service|xdg:{desktopId}", item.Id);
        Assert.Equal(StartupItemSource.LinuxXdgAutostart, item.Source);
        Assert.True(item.IsEnabled); // either one starts it
    }

    [Fact]
    public void Merge_GenericLaunchers_DoNotCauseFalseMatches()
    {
        var items = LinuxStartupItemManager.Merge(
            [Entry("a.desktop", "flatpak run org.a.App"), Entry("b.desktop", "sh -c 'b'")],
            [Unit("c.service", "/usr/bin/flatpak run org.c.App"), Unit("d.service", "/bin/sh -c d")]);

        Assert.Equal(4, items.Count);
    }

    [Fact]
    public void Merge_BothDisabled_IsDisabled()
    {
        var item = Assert.Single(LinuxStartupItemManager.Merge(
            [Entry("x.desktop", "x", enabled: false)], [Unit("x.service", "x", enabled: false)]));
        Assert.False(item.IsEnabled);
    }

    [Theory]
    [InlineData("xdg:a.desktop", null, "a.desktop")]
    [InlineData("systemd-user:a.service", "a.service", null)]
    [InlineData("systemd-user:a.service|xdg:we|rd.desktop", "a.service", "we|rd.desktop")]
    public void ParseId_RoundTrips(string id, string? unit, string? desktopId)
    {
        Assert.Equal((unit, desktopId), LinuxStartupItemManager.ParseId(id));
    }

    [Theory]
    [InlineData("Registry:foo")]
    [InlineData("systemd-user:a.service|bogus")]
    [InlineData("")]
    public void ParseId_RejectsForeignIds(string id)
    {
        Assert.Throws<ArgumentException>(() => LinuxStartupItemManager.ParseId(id));
    }

    [Fact]
    public async Task EndToEnd_ListsAndTogglesMergedItem_InTempConfigHome()
    {
        string home = Path.Combine(_dir.Path, "home");
        string systemDir = Path.Combine(_dir.Path, "etc");
        _dir.WriteFile(Path.Combine("etc", "autostart", "syncthing.desktop"),
            "[Desktop Entry]\nType=Application\nName=Syncthing\nExec=/usr/bin/syncthing serve\n");

        bool unitEnabled = true;
        var fake = new FakeSystemctl(args =>
        {
            if (args.Contains("list-unit-files"))
            {
                return FakeSystemctl.Ok($$"""[{"unit_file":"syncthing.service","state":"{{(unitEnabled ? "enabled" : "disabled")}}"}]""");
            }

            if (args.Contains("disable")) { unitEnabled = false; return FakeSystemctl.Ok(); }
            if (args.Contains("enable")) { unitEnabled = true; return FakeSystemctl.Ok(); }
            if (args.Contains("is-enabled")) { return FakeSystemctl.Ok(unitEnabled ? "enabled" : "disabled"); }
            return FakeSystemctl.Fail("show not scripted");
        });

        var manager = new LinuxStartupItemManager(
            new XdgAutostartStore(new XdgEnvironment(home, [systemDir], ["GNOME"], [], [])),
            new SystemdUserUnits(fake));

        var item = Assert.Single(await manager.GetStartupItemsAsync());
        Assert.True(item.IsEnabled);

        await manager.SetEnabledAsync(item, false);
        item = Assert.Single(await manager.GetStartupItemsAsync());
        Assert.False(item.IsEnabled);
        Assert.False(unitEnabled);
        Assert.True(File.Exists(Path.Combine(home, "autostart", "syncthing.desktop")));

        await manager.SetEnabledAsync(item, true);
        item = Assert.Single(await manager.GetStartupItemsAsync());
        Assert.True(item.IsEnabled);
        Assert.True(unitEnabled);
        Assert.False(File.Exists(Path.Combine(home, "autostart", "syncthing.desktop")));
    }

    /// <summary>Read-only against the real machine: lists, never toggles. Proves the real systemctl output parses.</summary>
    [LinuxOnlyFact]
    public async Task RealMachine_ListingDoesNotThrow()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var items = await LinuxStartupItemManager.CreateForCurrentProcess().GetStartupItemsAsync();

        Assert.All(items, i => Assert.True(
            i.Source is StartupItemSource.LinuxXdgAutostart or StartupItemSource.LinuxSystemdUser));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
    }
}
